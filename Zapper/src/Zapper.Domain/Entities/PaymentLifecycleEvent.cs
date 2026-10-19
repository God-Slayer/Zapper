using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class PaymentLifecycleEvent
{
    public Guid Id { get; init; }
    public Guid PaymentId { get; init; }
    public PaymentEventType Type { get; init; }
    public long? AmountMinorUnits { get; init; }
    public string CurrencyCode { get; init; } = "ZAR";
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? ExternalEventId { get; init; }
    public string? ResponseCode { get; init; }
    public string? Description { get; init; }
}