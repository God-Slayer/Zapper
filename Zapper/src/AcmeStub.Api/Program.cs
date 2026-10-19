using AcmeStub.Api;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<StubStore>();
builder.Services.AddSingleton<StubOptions>();
builder.Services.AddHttpClient();
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = null,
    DictionaryKeyPolicy = null,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "1";
        await context.HttpContext.Response.WriteAsJsonAsync(Error("E29", "The gateway request limit was exceeded."), jsonOptions, cancellationToken);
    };
    options.AddFixedWindowLimiter("acme-provider", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromSeconds(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "acme-stub" }));

app.MapPost("/api/payment", async (HttpContext context, StubStore store, StubOptions options) =>
{
    if (!IsBearerAuthorized(context, options.ApiKey))
        return Results.Json(Error("E02", "Unauthorized"), jsonOptions, statusCode: StatusCodes.Status401Unauthorized);

    AcmePaymentRequestEnvelope? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<AcmePaymentRequestEnvelope>(context.Request.Body, jsonOptions, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.Json(Error("E01", "Invalid JSON request"), jsonOptions, statusCode: StatusCodes.Status400BadRequest);
    }

    if (request?.PaymentRequest is not { } payment ||
        !IsValidPaymentRequest(request, payment) ||
        string.IsNullOrWhiteSpace(payment.TransactionID) ||
        string.IsNullOrWhiteSpace(payment.OrderNumber) ||
        payment.OrderNumber.Length > 50 ||
        payment.Amount <= 0)
        return Results.Json(Error("E01", "Missing or invalid required payment fields"), jsonOptions, statusCode: StatusCodes.Status400BadRequest);

    if (!store.TryRegisterMessageId(request.MessageHeader.MessageID))
        return Results.Json(Error("E01", "MessageID must be unique for each request."), jsonOptions, statusCode: StatusCodes.Status400BadRequest);

    if (store.TryUseThrottle())
    {
        context.Response.Headers["Retry-After"] = "1";
        return Results.Json(Error("E29", "The gateway request limit was exceeded."), jsonOptions, statusCode: StatusCodes.Status429TooManyRequests);
    }

    var rrn = payment.OrderNumber[..Math.Min(12, payment.OrderNumber.Length)];
    var result = store.Submit(payment, rrn, options);
    if (result.Error is not null)
        return Results.Json(Error(result.Error.Code, result.Error.Message, result.Error.OtherTransactionId), jsonOptions, statusCode: result.Error.StatusCode);

    if (result.Delay > TimeSpan.Zero)
    {
        try { await Task.Delay(result.Delay, context.RequestAborted); }
        catch (OperationCanceledException) { return Results.Empty; }
    }

    if (result.DropResponse)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
        catch (OperationCanceledException) { return Results.Empty; }
    }

    return Results.Json(ToEnvelope(result.Transaction!), jsonOptions);
}).RequireRateLimiting("acme-provider");

app.MapGet("/apirttq/query/Merchant/{merchantId}", (HttpContext context, string merchantId, string? TransactionID, string? OrderNumber, StubStore store, StubOptions options) =>
{
    if (!IsBearerAuthorized(context, options.ApiKey))
        return Results.Json(Error("E02", "Unauthorized"), jsonOptions, statusCode: StatusCodes.Status401Unauthorized);
    if (string.IsNullOrWhiteSpace(TransactionID) && string.IsNullOrWhiteSpace(OrderNumber))
        return Results.Json(Error("E01", "TransactionID or OrderNumber is required"), jsonOptions, statusCode: StatusCodes.Status400BadRequest);

    var tx = store.Find(merchantId, TransactionID, OrderNumber);
    return tx is null
        ? Results.Json(Error("E04", "Transaction not found"), jsonOptions, statusCode: StatusCodes.Status404NotFound)
        : Results.Json(ToEnvelope(tx), jsonOptions);
}).RequireRateLimiting("acme-provider");

// Local simulator controls: intended only for development, never exposed by the assignment-facing API.
app.MapPut("/__control/transactions/{transactionId}/scenario", (string transactionId, ScenarioRequest scenario, StubStore store) =>
{
    if (!Enum.TryParse<StubScenario>(scenario.Outcome, true, out var outcome))
        return Results.BadRequest(new { error = "Outcome must be Approved, Declined, InsufficientFunds, ExpiredCard, IssuerUnavailable, Processing, EftComplete, EftPending, Reversed, Refunded, GatewayFailure, Delayed, or Timeout." });
    if (scenario.DelayMilliseconds is < 0 or > 120_000)
        return Results.BadRequest(new { error = "DelayMilliseconds must be between 0 and 120000." });
    if (scenario.AuthAmount is <= 0)
        return Results.BadRequest(new { error = "AuthAmount must be positive when supplied." });
    store.SetScenario(transactionId, new TransactionScenario(outcome, scenario.DelayMilliseconds, scenario.AuthAmount));
    return Results.Ok(new { transactionId, outcome = outcome.ToString(), scenario.DelayMilliseconds, scenario.AuthAmount });
});

app.MapPut("/__control/default-scenario", (ScenarioRequest scenario, StubStore store) =>
{
    if (!Enum.TryParse<StubScenario>(scenario.Outcome, true, out var outcome))
        return Results.BadRequest(new { error = "Outcome must be Approved, Declined, InsufficientFunds, ExpiredCard, IssuerUnavailable, Processing, EftComplete, EftPending, Reversed, Refunded, GatewayFailure, Delayed, or Timeout." });
    if (scenario.DelayMilliseconds is < 0 or > 120_000)
        return Results.BadRequest(new { error = "DelayMilliseconds must be between 0 and 120000." });
    if (scenario.AuthAmount is <= 0)
        return Results.BadRequest(new { error = "AuthAmount must be positive when supplied." });
    store.SetDefaultScenario(new TransactionScenario(outcome, scenario.DelayMilliseconds, scenario.AuthAmount));
    return Results.Ok(new { outcome = outcome.ToString(), scenario.DelayMilliseconds, scenario.AuthAmount });
});

app.MapPost("/__control/throttle-next", (ThrottleRequest request, StubStore store) =>
{
    if (request.Count is < 1 or > 5) return Results.BadRequest(new { error = "Count must be between 1 and 5." });
    store.ThrottleNext(request.Count);
    return Results.Ok(new { throttledRequests = request.Count });
});

app.MapPost("/__control/transactions/{transactionId}/payment-advice", async (string transactionId, bool? duplicate, bool? stale, StubStore store, StubOptions options, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    var tx = store.FindByTransactionId(transactionId);
    if (tx is null) return Results.NotFound(new { error = "Transaction not found" });
    var advice = ToAdvice(tx, stale == true);
    var deliveries = duplicate == true ? 2 : 1;
    var results = new List<object>();
    for (var i = 0; i < deliveries; i++)
    {
        var payload = JsonSerializer.Serialize(advice, jsonOptions);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.CallbackBaseUrl.TrimEnd('/')}/api/notifications/payment-advice")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AdviceUsername}:{options.AdvicePassword}")));
        try
        {
            using var response = await clients.CreateClient().SendAsync(message, cancellationToken);
            results.Add(new { statusCode = (int)response.StatusCode, payload });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem($"Could not deliver Payment Advice to {options.CallbackBaseUrl}: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
    }
    return Results.Ok(new { destination = $"{options.CallbackBaseUrl}/api/notifications/payment-advice", deliveries = results });
});

app.MapPost("/__control/transactions/{transactionId}/transaction-status", async (string transactionId, TransactionStatusControlRequest request, StubStore store, StubOptions options, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    var tx = store.FindByTransactionId(transactionId);
    if (tx is null) return Results.NotFound(new { error = "Transaction not found" });
    var type = request.TransactionType?.Trim();
    if (!string.Equals(type, "Reversal", StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "Refund", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "TransactionType must be Reversal or Refund." });
    var amount = request.TransactionAmount ?? tx.Amount;
    if (amount <= 0 || amount > tx.Amount)
        return Results.BadRequest(new { error = "TransactionAmount must be positive and no greater than the original amount." });

    var notification = new TransactionStatusNotification
    {
        TransactionUUID = request.TransactionUUID ?? Guid.NewGuid().ToString(),
        TransactionType = type!,
        TransactionDateTime = SouthAfricanTimestamp.Format(DateTimeOffset.UtcNow),
        RetrievalReferenceNumber = tx.Rrn,
        TransactionAmount = amount,
        CurrencyCode = tx.CurrencyCode,
        TransactionResultCode = request.Approved == false ? "05" : "00",
        IsApproved = request.Approved != false
    };
    var payload = JsonSerializer.SerializeToUtf8Bytes(notification, jsonOptions);
    var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookSecret), payload));
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.CallbackBaseUrl.TrimEnd('/')}/api/notifications/transaction-status")
    {
        Content = new ByteArrayContent(payload)
    };
    message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    message.Headers.TryAddWithoutValidation("x-signature", signature);
    try
    {
        using var response = await clients.CreateClient().SendAsync(message, cancellationToken);
        return Results.Ok(new { destination = $"{options.CallbackBaseUrl}/api/notifications/transaction-status", statusCode = (int)response.StatusCode, headers = new Dictionary<string, string> { ["x-signature"] = signature }, payload = Encoding.UTF8.GetString(payload) });
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"Could not deliver transaction status notification to {options.CallbackBaseUrl}: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/__control/transactions", (StubStore store) => Results.Ok(store.All().Select(tx => new
{
    tx.MerchantId,
    tx.TransactionId,
    tx.OrderNumber,
    tx.Rrn,
    tx.Amount,
    tx.CurrencyCode,
    tx.CardAcceptorName,
    outcome = tx.Scenario.Outcome.ToString(),
    transactionStatus = tx.TransactionStatus
})));

app.Run();

static bool IsBearerAuthorized(HttpContext context, string expected)
{
    var authorization = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
    var actual = Encoding.UTF8.GetBytes(authorization[prefix.Length..].Trim());
    var wanted = Encoding.UTF8.GetBytes(expected);
    return actual.Length == wanted.Length && CryptographicOperations.FixedTimeEquals(actual, wanted);
}

static bool IsValidPaymentRequest(AcmePaymentRequestEnvelope envelope, AcmePaymentRequest payment)
{
    var header = envelope.MessageHeader;
    if (!DateTime.TryParseExact(header.MessageDateTime, "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        || string.IsNullOrWhiteSpace(header.MessageID)
        || header.MessageID.Length > 100 || header.Channel is not ("App" or "Web" or "POS")) return false;
    if (!Guid.TryParse(payment.MerchantID, out _) || payment.TransactionID.Length > 50
        || payment.OrderNumber.Length < 12 || payment.OrderNumber.Any(character => !char.IsAsciiLetterOrDigit(character))) return false;
    if (payment.CurrencyCode is { } currency && (currency.Length != 3 || currency.Any(character => !char.IsAsciiLetter(character)))) return false;
    var services = new[] { "CardNotPresent", "CardNotPresentRecurring", "AcmeEft", "GooglePay", "ApplePay" };
    if (!services.Contains(payment.PaymentService, StringComparer.Ordinal)) return false;
    var cardServices = new[] { "CardNotPresent", "CardNotPresentRecurring", "GooglePay", "ApplePay" };
    if (cardServices.Contains(payment.PaymentService, StringComparer.Ordinal) && !HasCardToken(payment.Card)) return false;
    if (payment.CardOnFileScenario is { } scenario && scenario is not ("CIT" or "MIT")) return false;
    if (string.IsNullOrWhiteSpace(payment.SubMerchantID) || payment.SubMerchantID.Length > 15
        || string.IsNullOrWhiteSpace(payment.MerchantCategoryCode) || payment.MerchantCategoryCode.Length != 4
        || payment.MerchantCategoryCode.Any(character => !char.IsAsciiDigit(character))
        || string.IsNullOrWhiteSpace(payment.CardAcceptorName) || payment.CardAcceptorName.Length != 40
        || payment.Email is { Length: > 254 }) return false;
    return true;
}

static bool HasCardToken(JsonElement? card) => card is { ValueKind: JsonValueKind.Object } value
    && value.TryGetProperty("Token", out var token)
    && token.ValueKind == JsonValueKind.String
    && !string.IsNullOrWhiteSpace(token.GetString());

static object Error(string code, string message, string? otherTransactionId = null) => new
{
    MessageHeader = NewHeader(),
    PaymentResponse = new AcmePaymentResponse
    {
        TransactionID = otherTransactionId,
        TransactionStatus = "Failure",
        ResponseDetail = new ResponseDetail
        {
            Source = "Application",
            Code = code,
            Description = message,
            ClientMessage = "We could not process that payment."
        }
    }
};

static AcmePaymentResponseEnvelope ToEnvelope(StubTransaction tx) => new()
{
    MessageHeader = NewHeader(),
    PaymentResponse = new AcmePaymentResponse
    {
        TransactionID = tx.TransactionId,
        TransactionDateTime = tx.TransactionDateTime,
        OrderNumber = tx.OrderNumber,
        ReconID = tx.ReconId,
        RetrievalReferenceNumber = tx.Rrn,
        AuthCode = tx.Approved ? "A12345" : null,
        TransactionStatus = tx.TransactionStatus,
        ResponseDetail = new ResponseDetail
        {
            Source = tx.Scenario.Outcome is StubScenario.GatewayFailure ? "Gateway" : "Acquirer",
            Code = tx.ResponseCode,
            Description = tx.ResponseDescription,
            ClientMessage = tx.Approved ? "Approved" : tx.ResponseDescription
        },
        AuthAmount = tx.Approved || tx.Scenario.Outcome is StubScenario.Reversed or StubScenario.Refunded
            ? tx.Scenario.AuthAmount ?? tx.Amount
            : null
    }
};

static MessageHeader NewHeader() => new()
{
    MessageDateTime = SouthAfricanTimestamp.Format(DateTimeOffset.UtcNow),
    MessageID = Guid.NewGuid().ToString(),
    Channel = "App"
};

static PaymentAdviceNotification ToAdvice(StubTransaction tx, bool stale) => new()
{
    RequestType = "Payment",
    MerchantID = tx.MerchantId,
    TransactionID = tx.TransactionId,
    TransactionDateTime = stale ? SouthAfricanTimestamp.Format(DateTimeOffset.UtcNow.AddDays(-1)) : tx.TransactionDateTime,
    OrderNumber = tx.OrderNumber,
    ReconID = tx.ReconId,
    RetrievalReferenceNumber = tx.Rrn,
    SystemsTraceAuditNumber = tx.Rrn[..Math.Min(6, tx.Rrn.Length)],
    Amount = tx.Amount,
    CurrencyCode = tx.CurrencyCode,
    PaymentService = "CardNotPresent",
    TransactionStatus = tx.TransactionStatus switch
    {
        "Success" or "Complete" => "Success",
        "Failure" => "Failure",
        _ => "Other"
    },
    ResponseSource = "Acquirer",
    ResponseCode = tx.ResponseCode,
    ResponseDescription = tx.ResponseDescription,
    AuthCode = tx.Approved ? "A12345" : null,
    MaskedCardNumber = "************4242",
    CommProtocol = "REST"
};

namespace AcmeStub.Api
{
    public static class SouthAfricanTimestamp
    {
        private static readonly TimeZoneInfo Zone = ResolveTimeZone();

        public static string Format(DateTimeOffset value) =>
            TimeZoneInfo.ConvertTime(value, Zone).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

        private static TimeZoneInfo ResolveTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
        }
    }

    public sealed class StubOptions
    {
        public string ApiKey => Environment.GetEnvironmentVariable("ACME_API_KEY") ?? "local-acme-api-key";
        public string WebhookSecret => Environment.GetEnvironmentVariable("ACME_WEBHOOK_SECRET") ?? "local-acme-webhook-secret";
        public string AdviceUsername => Environment.GetEnvironmentVariable("ACME_ADVICE_USERNAME") ?? "local-advice-user";
        public string AdvicePassword => Environment.GetEnvironmentVariable("ACME_ADVICE_PASSWORD") ?? "local-advice-password";
        public string CallbackBaseUrl => Environment.GetEnvironmentVariable("ZAPPER_CALLBACK_BASE_URL") ?? "http://localhost:5280";
    }

    public sealed record ScenarioRequest(string Outcome, int? DelayMilliseconds, long? AuthAmount);
    public sealed record ThrottleRequest(int Count);
    public sealed record TransactionStatusControlRequest(string? TransactionType, long? TransactionAmount, bool? Approved, string? TransactionUUID);

    public enum StubScenario
    { Approved, Declined, InsufficientFunds, ExpiredCard, IssuerUnavailable, Processing, EftComplete, EftPending, Reversed, Refunded, GatewayFailure, Delayed, Timeout }

    public sealed record TransactionScenario(StubScenario Outcome, int? DelayMilliseconds = null, long? AuthAmount = null);
    public sealed record StubError(string Code, string Message, string? OtherTransactionId, int StatusCode);
    public sealed record SubmitResult(StubTransaction? Transaction, StubError? Error, TimeSpan Delay, bool DropResponse);

    public sealed class StubTransaction
    {
        public string MerchantId { get; init; }
        public string TransactionId { get; init; }
        public string OrderNumber { get; init; }
        public string Rrn { get; init; }
        public long Amount { get; init; }
        public string CurrencyCode { get; init; }
        public string CardAcceptorName { get; init; }
        public string TransactionDateTime { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string ReconId { get; init; }
        public TransactionScenario Scenario { get; set; }
        private bool DelayCompleted => Scenario.Outcome != StubScenario.Delayed || DateTimeOffset.UtcNow >= CreatedAt.AddMilliseconds(Scenario.DelayMilliseconds ?? 0);

        public bool Approved => Scenario.Outcome is StubScenario.Approved or StubScenario.EftComplete
            || Scenario.Outcome == StubScenario.Delayed && DelayCompleted;

        public string TransactionStatus => Scenario.Outcome switch
        {
            StubScenario.Processing or StubScenario.Timeout or StubScenario.EftPending => "Processing",
            StubScenario.Delayed when !DelayCompleted => "Processing",
            StubScenario.GatewayFailure => "Failure",
            StubScenario.EftComplete => "Complete",
            StubScenario.Reversed => "Reversed",
            StubScenario.Refunded => "Refunded",
            _ => "Success"
        };

        public string ResponseCode => Scenario.Outcome == StubScenario.EftComplete ? "COMPL" : Approved ? "00" : Scenario.Outcome switch
        {
            StubScenario.Processing or StubScenario.Timeout or StubScenario.EftPending => "PEND",
            StubScenario.Delayed when !DelayCompleted => "PEND",
            StubScenario.GatewayFailure => "E50",
            StubScenario.InsufficientFunds => "51",
            StubScenario.ExpiredCard => "54",
            StubScenario.IssuerUnavailable => "91",
            StubScenario.EftComplete => "COMPL",
            StubScenario.Reversed or StubScenario.Refunded => "00",
            _ => "05"
        };

        public string ResponseDescription => Scenario.Outcome == StubScenario.EftComplete ? "EFT collection completed" : Approved ? "Approved" : Scenario.Outcome switch
        {
            StubScenario.Processing or StubScenario.Timeout or StubScenario.EftPending => "EFT collection is pending",
            StubScenario.Delayed when !DelayCompleted => "Transaction is still processing",
            StubScenario.GatewayFailure => "Simulated gateway error",
            StubScenario.InsufficientFunds => "Insufficient funds",
            StubScenario.ExpiredCard => "Expired card",
            StubScenario.IssuerUnavailable => "Issuer or switch inoperative",
            StubScenario.Reversed => "Transaction reversed",
            StubScenario.Refunded => "Transaction refunded",
            _ => "Do not honour"
        };
    }

    public sealed class StubStore
    {
        private readonly object _submissionLock = new();
        private readonly ConcurrentDictionary<string, StubTransaction> _byTransaction = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _rrnOwners = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TransactionScenario> _scenarios = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _messageIds = new(StringComparer.Ordinal);
        private TransactionScenario _defaultScenario = new(StubScenario.Approved);
        private int _throttleCount;

        private static string Key(string merchant, string tx) => $"{merchant}:{tx}";

        public SubmitResult Submit(AcmePaymentRequest payment, string rrn, StubOptions options)
        {
            // The ownership check and transaction insert must be one atomic operation. The stub
            // handles at most 20 requests/second, so serializing this short in-memory section is
            // preferable to allowing two concurrent requests to claim the same account RRN.
            lock (_submissionLock)
                return SubmitUnderLock(payment, rrn, options);
        }

        private SubmitResult SubmitUnderLock(AcmePaymentRequest payment, string rrn, StubOptions options)
        {
            var key = Key(payment.MerchantID, payment.TransactionID);
            if (_byTransaction.TryGetValue(key, out var existing))
            {
                if (existing.OrderNumber != payment.OrderNumber || existing.Amount != payment.Amount || existing.CurrencyCode != (payment.CurrencyCode ?? "ZAR"))
                    return new(null, new("E09", "TransactionID was already used with a different request", existing.TransactionId, 409), TimeSpan.Zero, false);
                return ScenarioResult(existing);
            }

            if (payment.OrderNumber.Length < 12 || payment.OrderNumber.Take(12).Any(c => !char.IsAsciiLetterOrDigit(c)))
                return new(null, new("E01", "OrderNumber must begin with a 12-character alphanumeric RRN", null, 400), TimeSpan.Zero, false);
            var owner = Key(payment.MerchantID, payment.OrderNumber[..12]);
            if (_rrnOwners.TryGetValue(owner, out var other) && other != key)
                return new(null, new("E09", "Retrieval reference number already exists", other[(payment.MerchantID.Length + 1)..], 409), TimeSpan.Zero, false);

            var scenario = _scenarios.GetValueOrDefault(payment.TransactionID, Volatile.Read(ref _defaultScenario));
            if (scenario.Outcome == StubScenario.GatewayFailure)
                return new(null, new("E50", "The gateway is temporarily degraded.", null, 503), TimeSpan.Zero, false);
            var tx = new StubTransaction
            {
                MerchantId = payment.MerchantID,
                TransactionId = payment.TransactionID,
                OrderNumber = payment.OrderNumber,
                Rrn = payment.OrderNumber[..12],
                Amount = payment.Amount,
                CurrencyCode = payment.CurrencyCode ?? "ZAR",
                CardAcceptorName = payment.CardAcceptorName ?? string.Empty,
                TransactionDateTime = SouthAfricanTimestamp.Format(DateTimeOffset.UtcNow),
                CreatedAt = DateTimeOffset.UtcNow,
                ReconId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Scenario = scenario
            };
            if (!_byTransaction.TryAdd(key, tx)) return ScenarioResult(_byTransaction[key]);
            if (!_rrnOwners.TryAdd(owner, key))
            {
                _byTransaction.TryRemove(key, out _);
                var existingOwner = _rrnOwners[owner];
                return new(null, new("E09", "Retrieval reference number already exists", existingOwner[(payment.MerchantID.Length + 1)..], 409), TimeSpan.Zero, false);
            }
            return ScenarioResult(tx);
        }

        public void SetScenario(string id, TransactionScenario scenario)
        {
            _scenarios[id] = scenario;
            var existing = _byTransaction.Values.FirstOrDefault(x => x.TransactionId == id);
            if (existing is not null) existing.Scenario = scenario;
        }

        public void SetDefaultScenario(TransactionScenario scenario) => Volatile.Write(ref _defaultScenario, scenario);

        public bool TryRegisterMessageId(string messageId) => _messageIds.TryAdd(messageId, 0);

        public void ThrottleNext(int count) => Interlocked.Exchange(ref _throttleCount, count);

        public bool TryUseThrottle()
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _throttleCount);
                if (remaining <= 0) return false;
                if (Interlocked.CompareExchange(ref _throttleCount, remaining - 1, remaining) == remaining) return true;
            }
        }

        public StubTransaction? Find(string merchant, string? transactionId, string? orderNumber) =>
            _byTransaction.Values.FirstOrDefault(x => x.MerchantId == merchant &&
                ((!string.IsNullOrWhiteSpace(transactionId) && x.TransactionId == transactionId) ||
                 (!string.IsNullOrWhiteSpace(orderNumber) && x.OrderNumber == orderNumber)));

        public StubTransaction? FindByTransactionId(string id) => _byTransaction.Values.FirstOrDefault(x => x.TransactionId == id);

        public IReadOnlyCollection<StubTransaction> All() => _byTransaction.Values.ToArray();

        private static SubmitResult ScenarioResult(StubTransaction tx) => new(tx, null,
            tx.Scenario.Outcome is StubScenario.Delayed or StubScenario.Timeout ? TimeSpan.FromMilliseconds(tx.Scenario.DelayMilliseconds ?? (tx.Scenario.Outcome == StubScenario.Timeout ? 30_000 : 0)) : TimeSpan.Zero,
            tx.Scenario.Outcome == StubScenario.Timeout);
    }

    public sealed record AcmePaymentRequestEnvelope
    {
        public MessageHeader MessageHeader { get; init; } = new();
        public AcmePaymentRequest PaymentRequest { get; init; } = new();
    }
    public sealed record AcmePaymentRequest
    {
        public string MerchantID { get; init; } = string.Empty;
        public string TransactionID { get; init; } = string.Empty;
        public string OrderNumber { get; init; } = string.Empty;
        public long Amount { get; init; }
        public string? CurrencyCode { get; init; }
        public string PaymentService { get; init; } = string.Empty;
        public JsonElement? Card { get; init; }
        public string? CardOnFileScenario { get; init; }
        public string? SubMerchantID { get; init; }
        public string? MerchantCategoryCode { get; init; }
        public string? CardAcceptorName { get; init; }
        public string? Email { get; init; }
    }
    public sealed record AcmePaymentResponseEnvelope
    {
        public MessageHeader MessageHeader { get; init; } = new();
        public AcmePaymentResponse PaymentResponse { get; init; } = new();
    }
    public sealed record AcmePaymentResponse
    {
        public string? TransactionID { get; init; }
        public string? TransactionDateTime { get; init; }
        public string? OrderNumber { get; init; }
        public string? ReconID { get; init; }
        public string? RetrievalReferenceNumber { get; init; }
        public string? AuthCode { get; init; }
        public string TransactionStatus { get; init; } = string.Empty;
        public ResponseDetail ResponseDetail { get; init; } = new();
        public long? AuthAmount { get; init; }
    }
    public sealed record ResponseDetail
    {
        public string Source { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ClientMessage { get; init; } = string.Empty;
    }
    public sealed record MessageHeader
    {
        public string MessageDateTime { get; init; } = string.Empty;
        public string MessageID { get; init; } = string.Empty;
        public string Channel { get; init; } = "App";
    }
    public sealed record PaymentAdviceNotification
    {
        public string RequestType { get; init; } = string.Empty;
        public string MerchantID { get; init; } = string.Empty;
        public string TransactionID { get; init; } = string.Empty;
        public string? TransactionDateTime { get; init; }
        public string? OrderNumber { get; init; }
        public string? ReconID { get; init; }
        public string RetrievalReferenceNumber { get; init; } = string.Empty;
        public string SystemsTraceAuditNumber { get; init; } = string.Empty;
        public long Amount { get; init; }
        public string? CurrencyCode { get; init; }
        public string PaymentService { get; init; } = string.Empty;
        public string TransactionStatus { get; init; } = string.Empty;
        public string ResponseSource { get; init; } = string.Empty;
        public string ResponseCode { get; init; } = string.Empty;
        public string? ResponseDescription { get; init; }
        public string? AuthCode { get; init; }
        public string? MaskedCardNumber { get; init; }
        public string CommProtocol { get; init; } = string.Empty;
    }
    public sealed record TransactionStatusNotification
    {
        [JsonPropertyName("transactionUUID")] public string TransactionUUID { get; init; } = string.Empty;
        [JsonPropertyName("transactionType")] public string TransactionType { get; init; } = string.Empty;
        [JsonPropertyName("transactionDateTime")] public string TransactionDateTime { get; init; } = string.Empty;
        [JsonPropertyName("rrn")] public string RetrievalReferenceNumber { get; init; } = string.Empty;
        [JsonPropertyName("transactionAmount")] public long TransactionAmount { get; init; }
        [JsonPropertyName("currencyCode")] public string CurrencyCode { get; init; } = string.Empty;
        [JsonPropertyName("txResultCode")] public string TransactionResultCode { get; init; } = string.Empty;
        [JsonPropertyName("isApproved")] public bool IsApproved { get; init; }
    }
}