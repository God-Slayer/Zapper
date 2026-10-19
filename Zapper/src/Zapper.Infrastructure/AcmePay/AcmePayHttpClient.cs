using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Zapper.Application.AcmePay;

namespace Zapper.Infrastructure.AcmePay;

public sealed class AcmePayHttpClient(HttpClient httpClient, AcmePayClientOptions options, ILogger<AcmePayHttpClient> logger) : IAcmePayClient
{
    private static readonly SpacedRequestGate RequestGate = new(TimeSpan.FromMilliseconds(67));
    private static readonly TimeSpan MaximumAutomaticRetryDelay = TimeSpan.FromSeconds(5);

    public async Task<AcmePaymentResponseEnvelope> SubmitPaymentAsync(AcmePaymentRequestEnvelope request, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Payment" });
        var requestForAttempt = request;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var body = JsonSerializer.Serialize(requestForAttempt, AcmeJsonSerializerOptions.Default);
                using var response = await SendAsync(HttpMethod.Post, "/api/payment", body, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw await CreateExceptionAsync(response, cancellationToken);
                return await ReadResponseAsync(response, cancellationToken);
            }
            catch (AcmePayException ex) when (attempt == 0 && ex.StatusCode == (int)HttpStatusCode.TooManyRequests
                && ex.RetryAfter is { } retryAfter && retryAfter <= MaximumAutomaticRetryDelay)
            {
                logger.LogWarning("Acme throttled transaction {AcmeTransactionId} with {AcmeResponseCode}; waiting {RetryAfterSeconds} seconds and retrying with the same transaction identifiers.",
                    request.PaymentRequest.TransactionID, ex.Code, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, cancellationToken);
                requestForAttempt = requestForAttempt with
                {
                    MessageHeader = requestForAttempt.MessageHeader with
                    {
                        MessageID = Guid.NewGuid().ToString(),
                        MessageDateTime = DateTimeOffset.UtcNow.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    }
                };
                logger.LogInformation("Retrying Acme message {AcmeMessageId} for transaction {AcmeTransactionId}.",
                    requestForAttempt.MessageHeader.MessageID, requestForAttempt.PaymentRequest.TransactionID);
            }
        }
    }

    public async Task<AcmePaymentResponseEnvelope?> QueryTransactionAsync(string merchantId, string? transactionId, string? orderNumber, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Payment" });
        var query = !string.IsNullOrWhiteSpace(transactionId)
            ? $"TransactionID={Uri.EscapeDataString(transactionId)}"
            : $"OrderNumber={Uri.EscapeDataString(orderNumber ?? string.Empty)}";
        var path = $"/apirttq/query/Merchant/{Uri.EscapeDataString(merchantId)}?{query}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<AcmePayException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = $"HTTP_{(int)response.StatusCode}";
        var message = $"Acme returned HTTP {(int)response.StatusCode}.";
        string? source = null;
        string? clientMessage = null;
        string? otherTransactionId = null;
        string? responseMessageId = null;
        string? transactionStatus = null;
        string? reconId = null;
        string? authCode = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("MessageHeader", out var header)
                && header.TryGetProperty("MessageID", out var responseIdElement))
                responseMessageId = responseIdElement.GetString();
            if (root.TryGetProperty("PaymentResponse", out var paymentResponse)
                && paymentResponse.TryGetProperty("ResponseDetail", out var detail))
            {
                if (detail.TryGetProperty("Code", out var codeElement)) code = codeElement.GetString() ?? code;
                if (detail.TryGetProperty("Description", out var messageElement)) message = messageElement.GetString() ?? message;
                if (detail.TryGetProperty("Source", out var sourceElement)) source = sourceElement.GetString();
                if (detail.TryGetProperty("ClientMessage", out var clientMessageElement)) clientMessage = clientMessageElement.GetString();
                if (paymentResponse.TryGetProperty("TransactionStatus", out var statusElement)) transactionStatus = statusElement.GetString();
                if (paymentResponse.TryGetProperty("ReconID", out var reconElement)) reconId = reconElement.GetString();
                if (paymentResponse.TryGetProperty("AuthCode", out var authElement)) authCode = authElement.GetString();
                if (string.Equals(code, "E09", StringComparison.OrdinalIgnoreCase)
                    && paymentResponse.TryGetProperty("TransactionID", out var transactionElement))
                    otherTransactionId = transactionElement.GetString();
            }
            else if (root.TryGetProperty("Error", out var error))
            {
                if (error.TryGetProperty("Code", out var codeElement)) code = codeElement.GetString() ?? code;
                if (error.TryGetProperty("Description", out var messageElement)) message = messageElement.GetString() ?? message;
                if (error.TryGetProperty("OtherTransactionID", out var otherIdElement)) otherTransactionId = otherIdElement.GetString();
            }
        }
        catch (JsonException) { }
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
        return new AcmePayException(code, message, (int)response.StatusCode, source, clientMessage, otherTransactionId,
            retryAfter, content, responseMessageId, transactionStatus, reconId, authCode);
    }

    private static async Task<AcmePaymentResponseEnvelope> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var envelope = JsonSerializer.Deserialize<AcmePaymentResponseEnvelope>(content, AcmeJsonSerializerOptions.Default);
            return envelope is null
                ? throw new AcmePayException("INVALID_RESPONSE", "Acme returned an empty payment response.", (int)HttpStatusCode.BadGateway, rawPayload: content)
                : envelope with { RawPayload = content };
        }
        catch (JsonException)
        {
            throw new AcmePayException("INVALID_RESPONSE", "Acme returned a malformed payment response.", (int)HttpStatusCode.BadGateway, rawPayload: content);
        }
    }

    private sealed class SpacedRequestGate(TimeSpan spacing)
    {
        private long _nextRequestTicks;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var now = DateTime.UtcNow.Ticks;
                var next = Volatile.Read(ref _nextRequestTicks);
                var slot = Math.Max(now, next);
                if (Interlocked.CompareExchange(ref _nextRequestTicks, slot + spacing.Ticks, next) != next) continue;
                var delay = slot - now;
                if (delay > 0) await Task.Delay(TimeSpan.FromTicks(delay), cancellationToken);
                return;
            }
        }
    }
}

public sealed class AcmePayClientOptions
{
    public string BaseUrl { get; init; } = "http://localhost:5291";
    public string ApiKey { get; init; } = "local-acme-api-key";
}