using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class Payment
{
    public Guid Id { get; init; }
    public Guid AppPaymentId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid SubMerchantId { get; init; }
    public long AmountMinorUnits { get; init; }
    public string CurrencyCode { get; init; } = "ZAR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string AcmeTransactionId { get; init; } = string.Empty;
    public string AcmeOrderNumber { get; init; } = string.Empty;
    public string? RetrievalReferenceNumber { get; set; }
    public long? AuthorizedAmountMinorUnits { get; set; }
    public string? AcmeResponseCode { get; set; }
    public string? AcmeResponseMessageId { get; set; }
    public string? AcmeTransactionStatus { get; set; }
    public string? AcmeResponseSource { get; set; }
    public string? AcmeResponseDescription { get; set; }
    public string? AcmeClientMessage { get; set; }
    public string? AcmeReconId { get; set; }
    public string? AcmeAuthCode { get; set; }
    public string? AcmeOtherTransactionId { get; set; }
    public string? AcmeResponsePayload { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastPaymentOutcomeAt { get; set; }
    public ICollection<PaymentLifecycleEvent> LifecycleEvents { get; init; } = new List<PaymentLifecycleEvent>();
}