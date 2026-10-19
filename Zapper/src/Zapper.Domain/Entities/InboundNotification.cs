using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class InboundNotification
{
    public Guid Id { get; init; }
    public NotificationSource Source { get; init; }
    public string SourceName { get; private set; } = string.Empty;
    public string DeduplicationKey { get; init; } = string.Empty;
    public string? ExternalEventId { get; init; }
    public string? AcmeTransactionId { get; init; }
    public string? RetrievalReferenceNumber { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public DateTimeOffset? ProviderOccurredAt { get; init; }
    public string PayloadSha256 { get; init; } = string.Empty;
    public string RawPayload { get; init; } = string.Empty;
    public NotificationProcessingStatus ProcessingStatus { get; set; } = NotificationProcessingStatus.Received;
    public string ProcessingStatusName { get; private set; } = string.Empty;
    public string? ProcessingNote { get; set; }
}
