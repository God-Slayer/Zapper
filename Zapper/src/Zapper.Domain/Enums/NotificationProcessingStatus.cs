namespace Zapper.Domain.Enums;

public enum NotificationProcessingStatus
{
    Received,
    Processed,
    IgnoredAsDuplicate,
    Rejected,
    Unmatched
}