using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zapper.Application.Abstractions;
using Zapper.Domain.Entities;
using Zapper.Infrastructure.Persistence;

namespace Zapper.Infrastructure.Repositories;

public sealed class PaymentRepository(ZapperDbContext dbContext) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.Payments.Include(x => x.LifecycleEvents).SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);

    public Task<Payment?> GetByAppPaymentIdAsync(Guid appPaymentId, CancellationToken cancellationToken) =>
        dbContext.Payments.Include(x => x.LifecycleEvents).SingleOrDefaultAsync(x => x.AppPaymentId == appPaymentId, cancellationToken);

    public Task<Payment?> GetByAcmeTransactionIdAsync(string transactionId, CancellationToken cancellationToken) =>
        dbContext.Payments.Include(x => x.LifecycleEvents).SingleOrDefaultAsync(x => x.AcmeTransactionId == transactionId, cancellationToken);

    public Task<Payment?> GetByRetrievalReferenceNumberAsync(string retrievalReferenceNumber, CancellationToken cancellationToken) =>
        dbContext.Payments.Include(x => x.LifecycleEvents).SingleOrDefaultAsync(x => x.RetrievalReferenceNumber == retrievalReferenceNumber, cancellationToken);

    public async Task LockByAcmeTransactionIdAsync(string transactionId, CancellationToken cancellationToken) =>
        _ = await dbContext.Payments.FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"AcmeTransactionId\" = {transactionId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    public async Task LockByRetrievalReferenceNumberAsync(string retrievalReferenceNumber, CancellationToken cancellationToken) =>
        _ = await dbContext.Payments.FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"RetrievalReferenceNumber\" = {retrievalReferenceNumber} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    public Task AddAsync(Payment payment, CancellationToken cancellationToken) => dbContext.Payments.AddAsync(payment, cancellationToken).AsTask();

    public async Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        await dbContext.Payments.AddAsync(payment, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            foreach (var lifecycleEvent in payment.LifecycleEvents)
                dbContext.Entry(lifecycleEvent).State = EntityState.Detached;
            dbContext.Entry(payment).State = EntityState.Detached;
            return false;
        }
    }

    public Task AddLifecycleEventAsync(PaymentLifecycleEvent lifecycleEvent, CancellationToken cancellationToken) =>
        dbContext.PaymentLifecycleEvents.AddAsync(lifecycleEvent, cancellationToken).AsTask();

    private static bool IsUniqueViolation(Exception exception) => exception switch
    {
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => true,
        Zapper.Application.Abstractions.UniqueConstraintViolationException { InnerException: DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } } => true,
        _ => false
    };
}