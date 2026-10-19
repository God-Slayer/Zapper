using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapper.Application.AcmePay;

public sealed record AcmePaymentRequestEnvelope
{
    public AcmeMessageHeader MessageHeader { get; init; } = new();
    public AcmePaymentRequest PaymentRequest { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record AcmePaymentRequest
{
    public string MerchantID { get; init; } = string.Empty;
    public string TransactionID { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public long Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string PaymentService { get; init; } = "CardNotPresent";
    public AcmeCard? Card { get; init; }
    public string? CardOnFileScenario { get; init; }
    public string? SubMerchantID { get; init; }
    public string? MerchantCategoryCode { get; init; }
    public string? CardAcceptorName { get; init; }
    public string? Email { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record AcmeCard
{
    public string Token { get; init; } = string.Empty;
}

public sealed record AcmePaymentResponseEnvelope
{
    public AcmeMessageHeader MessageHeader { get; init; } = new();
    public AcmePaymentResponse PaymentResponse { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }

    [JsonIgnore]
    public string? RawPayload { get; init; }
}

public sealed record AcmePaymentResponse
{
    public string? TransactionID { get; init; }
    public string? TransactionDateTime { get; init; }
    public string? OrderNumber { get; init; }
    public string? ReconID { get; init; }
    public string? RetrievalReferenceNumber { get; init; }
    public string? AuthCode { get; init; }
    public string? TransactionStatus { get; init; }
    public AcmeResponseDetail? ResponseDetail { get; init; }
    public long? AuthAmount { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record AcmeResponseDetail
{
    public string? Source { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? ClientMessage { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}