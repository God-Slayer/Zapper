using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Zapper.Application.AcmePay;
using Zapper.Application.Notifications;

namespace Zapper.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(NotificationApplicationService notificationService, ILogger<NotificationsController> logger) : ControllerBase
{
    [HttpPost("payment-advice")]
    [Consumes("application/json")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> PaymentAdvice(CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Notification" });
        if (!TryReadBasicCredentials(Request.Headers.Authorization, out var username, out var password) ||
            !notificationService.IsValidPaymentAdviceCredential(username, password))
            return Unauthorized();

        var payload = await ReadRawBodyAsync(Request, cancellationToken);
        AcmePaymentAdviceNotification? advice;
        try { advice = JsonSerializer.Deserialize<AcmePaymentAdviceNotification>(payload, AcmeJsonSerializerOptions.Default); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_NOTIFICATION", message = "Notification body is not valid JSON." }); }
        if (advice is null) return BadRequest(new { code = "INVALID_NOTIFICATION", message = "Notification body is required." });

        try
        {
            await notificationService.ProcessPaymentAdviceAsync(advice, System.Text.Encoding.UTF8.GetString(payload), cancellationToken);
            logger.LogInformation("Payment Advice received for Acme transaction {AcmeTransactionId} with provider status {AcmeTransactionStatus} and code {AcmeResponseCode}.",
                advice.TransactionID, advice.TransactionStatus, advice.ResponseCode);
            return Ok();
        }
        catch (Zapper.Application.Payments.PaymentApplicationException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("transaction-status")]
    [Consumes("application/json")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> TransactionStatus(CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Notification" });
        var payload = await ReadRawBodyAsync(Request, cancellationToken);
        if (!notificationService.IsValidWebhookSignature(payload, Request.Headers["x-signature"].FirstOrDefault()))
            return Unauthorized();

        AcmeTransactionStatusNotification? notification;
        try { notification = JsonSerializer.Deserialize<AcmeTransactionStatusNotification>(payload, AcmeJsonSerializerOptions.Default); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_NOTIFICATION", message = "Notification body is not valid JSON." }); }
        if (notification is null) return BadRequest(new { code = "INVALID_NOTIFICATION", message = "Notification body is required." });

        try
        {
            await notificationService.ProcessTransactionStatusAsync(notification, System.Text.Encoding.UTF8.GetString(payload), cancellationToken);
            logger.LogInformation("Acme {TransactionType} event {AcmeEventId} received for RRN {RetrievalReferenceNumber} with result {AcmeResponseCode}.",
                notification.TransactionType, notification.TransactionUUID, notification.RetrievalReferenceNumber, notification.TransactionResultCode);
            return Ok();
        }
        catch (Zapper.Application.Payments.PaymentApplicationException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    private static async Task<byte[]> ReadRawBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static bool TryReadBasicCredentials(string? authorization, out string? username, out string? password)
    {
        username = null;
        password = null;
        if (authorization is null || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
            var separator = decoded.IndexOf(':');
            if (separator < 0) return false;
            username = decoded[..separator];
            password = decoded[(separator + 1)..];
            return true;
        }
        catch (FormatException) { return false; }
        catch (System.Text.DecoderFallbackException) { return false; }
    }
}