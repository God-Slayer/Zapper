using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class PaymentLifecycleEventTypeLookup
{
    public PaymentEventType EventTypeCode { get; init; }
    public string EventTypeName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
