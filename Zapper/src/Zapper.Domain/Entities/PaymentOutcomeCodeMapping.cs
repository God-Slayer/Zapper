using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class PaymentOutcomeCodeMapping
{
    public string ResponseCode { get; init; } = string.Empty;
    public string TransactionStatusContext { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public PaymentStatus? PaymentStatusCode { get; init; }
    public PaymentEventType? EventTypeCode { get; init; }
}
