using Zapper.Domain.Entities;

namespace Zapper.Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<Payment?> GetByAppPaymentIdAsync(Guid appPaymentId, CancellationToken cancellationToken);

    Task<Payment?> GetByAcmeTransactionIdAsync(string transactionId, CancellationToken cancellationToken);

    Task<Payment?> GetByRetrievalReferenceNumberAsync(string retrievalReferenceNumber, CancellationToken cancellationToken);

    Task LockByAcmeTransactionIdAsync(string transactionId, CancellationToken cancellationToken);

    Task LockByRetrievalReferenceNumberAsync(string retrievalReferenceNumber, CancellationToken cancellationToken);

    Task AddAsync(Payment payment, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken);

    Task AddLifecycleEventAsync(PaymentLifecycleEvent lifecycleEvent, CancellationToken cancellationToken);
}