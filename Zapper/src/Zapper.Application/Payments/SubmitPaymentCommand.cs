namespace Zapper.Application.Payments;

public sealed record SubmitPaymentCommand(
    Guid CustomerId,
    string ZapperMerchantId,
    long AmountMinorUnits,
    string? CurrencyCode,
    Guid AppPaymentId);