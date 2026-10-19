namespace Zapper.Api.Controllers.Payments;

public sealed record SubmitPaymentRequest(
    Guid CustomerId,
    string SubMerchantId,
    long Amount,
    Guid AppPaymentId,
    string? CurrencyCode);

public sealed record PaymentStatusResponse(
    Guid PaymentId,
    Guid AppPaymentId,
    string Status,
    long Amount,
    string CurrencyCode,
    long? AuthorizedAmount,
    string? AcmeResponseCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AcmeMessageId,
    string? AcmeTransactionStatus,
    string? AcmeResponseSource,
    string? AcmeResponseDescription,
    string? AcmeClientMessage,
    string? AcmeReconId,
    string? AcmeAuthCode,
    string? AcmeOtherTransactionId);