namespace Zapper.Domain.Enums;

public enum PaymentStatus
{
    Pending,
    Processing,
    OutcomeUnknown,
    Approved,
    Declined,
    SubmissionFailed,
    Reversed,
    PartiallyRefunded,
    Refunded
}