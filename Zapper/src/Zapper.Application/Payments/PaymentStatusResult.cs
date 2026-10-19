using Zapper.Domain.Enums;

namespace Zapper.Application.Payments;

public sealed record PaymentStatusResult(
    Guid PaymentId,
    Guid AppPaymentId,
    PaymentStatus Status,
    long AmountMinorUnits,
    string CurrencyCode,
    long? AuthorizedAmountMinorUnits,
    string? AcmeResponseCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AcmeMessageId = null,
    string? AcmeTransactionStatus = null,
    string? AcmeResponseSource = null,
    string? AcmeResponseDescription = null,
    string? AcmeClientMessage = null,
    string? AcmeReconId = null,
    string? AcmeAuthCode = null,
    string? AcmeOtherTransactionId = null);