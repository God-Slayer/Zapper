namespace Zapper.Domain.Enums;

public enum PaymentEventType
{
    Submitted,
    Processing,
    OutcomeUnknown,
    Approved,
    Declined,
    SubmissionFailed,
    Reversed,
    RefundApplied
}