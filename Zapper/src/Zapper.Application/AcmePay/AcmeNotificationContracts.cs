using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapper.Application.AcmePay;

public sealed record AcmePaymentAdviceNotification
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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record AcmeTransactionStatusNotification
{
    [JsonPropertyName("transactionUUID")]
    public string TransactionUUID { get; init; } = string.Empty;

    [JsonPropertyName("transactionType")]
    public string TransactionType { get; init; } = string.Empty;

    [JsonPropertyName("transactionDateTime")]
    public string TransactionDateTime { get; init; } = string.Empty;

    [JsonPropertyName("rrn")]
    public string RetrievalReferenceNumber { get; init; } = string.Empty;

    [JsonPropertyName("transactionAmount")]
    public long TransactionAmount { get; init; }

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; init; } = string.Empty;

    [JsonPropertyName("txResultCode")]
    public string TransactionResultCode { get; init; } = string.Empty;

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}