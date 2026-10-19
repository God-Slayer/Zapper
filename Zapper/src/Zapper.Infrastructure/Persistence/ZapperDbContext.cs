using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zapper.Application.Abstractions;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Infrastructure.Persistence;

public sealed class ZapperDbContext(DbContextOptions<ZapperDbContext> options) : DbContext(options), IUnitOfWork
{
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try { return await base.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new Zapper.Application.Abstractions.UniqueConstraintViolationException(ex);
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfUnitOfWorkTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StoredCard> StoredCards => Set<StoredCard>();
    public DbSet<SubMerchant> SubMerchants => Set<SubMerchant>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentLifecycleEvent> PaymentLifecycleEvents => Set<PaymentLifecycleEvent>();
    public DbSet<InboundNotification> InboundNotifications => Set<InboundNotification>();
    public DbSet<PaymentStatusLookup> PaymentStatuses => Set<PaymentStatusLookup>();
    public DbSet<PaymentLifecycleEventTypeLookup> PaymentLifecycleEventTypes => Set<PaymentLifecycleEventTypeLookup>();
    public DbSet<PaymentOutcomeCodeMapping> PaymentOutcomeCodeMappings => Set<PaymentOutcomeCodeMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.EmailAddress).IsUnique();
            entity.HasMany(x => x.StoredCards).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoredCard>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AcmeToken).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.AcmeToken).IsUnique();
            entity.HasIndex(x => x.CustomerId);
        });

        modelBuilder.Entity<SubMerchant>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ZapperMerchantId).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TradingName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RegionCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.MerchantCategoryCode).HasMaxLength(4).IsRequired();
            entity.HasIndex(x => x.ZapperMerchantId).IsUnique();
        });

        modelBuilder.Entity<PaymentStatusLookup>(entity =>
        {
            entity.ToTable("PaymentStatuses");
            entity.HasKey(x => x.StatusCode);
            entity.Property(x => x.StatusName).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasData(
                new PaymentStatusLookup { StatusCode = PaymentStatus.Pending, StatusName = "Pending", Description = "Payment has been recorded and is awaiting an outcome." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.Processing, StatusName = "Processing", Description = "The provider reports that payment processing is still underway." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.OutcomeUnknown, StatusName = "OutcomeUnknown", Description = "The provider outcome could not be confirmed." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.Approved, StatusName = "Approved", Description = "Payment was approved." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.Declined, StatusName = "Declined", Description = "Payment was declined." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.SubmissionFailed, StatusName = "SubmissionFailed", Description = "The payment submission failed or was rejected." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.Reversed, StatusName = "Reversed", Description = "The payment was reversed." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.PartiallyRefunded, StatusName = "PartiallyRefunded", Description = "Part of the authorized amount was refunded." },
                new PaymentStatusLookup { StatusCode = PaymentStatus.Refunded, StatusName = "Refunded", Description = "The payment was fully refunded." });
        });

        modelBuilder.Entity<PaymentLifecycleEventTypeLookup>(entity =>
        {
            entity.ToTable("PaymentLifecycleEventTypes");
            entity.HasKey(x => x.EventTypeCode);
            entity.Property(x => x.EventTypeName).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasData(
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.Submitted, EventTypeName = "Submitted", Description = "Zapper recorded the payment submission." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.Processing, EventTypeName = "Processing", Description = "The provider reports that processing is underway." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.OutcomeUnknown, EventTypeName = "OutcomeUnknown", Description = "Zapper could not confirm the payment outcome." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.Approved, EventTypeName = "Approved", Description = "An approval outcome was recorded." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.Declined, EventTypeName = "Declined", Description = "A decline outcome was recorded." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.SubmissionFailed, EventTypeName = "SubmissionFailed", Description = "A payment submission failure was recorded." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.Reversed, EventTypeName = "Reversed", Description = "A payment reversal was recorded." },
                new PaymentLifecycleEventTypeLookup { EventTypeCode = PaymentEventType.RefundApplied, EventTypeName = "RefundApplied", Description = "A refund event was recorded." });
        });

        modelBuilder.Entity<PaymentOutcomeCodeMapping>(entity =>
        {
            entity.ToTable("PaymentOutcomeCodeMappings");
            entity.HasKey(x => new { x.ResponseCode, x.TransactionStatusContext });
            entity.Property(x => x.ResponseCode).HasMaxLength(20);
            entity.Property(x => x.TransactionStatusContext).HasMaxLength(50);
            entity.Property(x => x.Origin).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Meaning).HasMaxLength(250).IsRequired();
            entity.HasOne<PaymentStatusLookup>()
                .WithMany()
                .HasForeignKey(x => x.PaymentStatusCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentLifecycleEventTypeLookup>()
                .WithMany()
                .HasForeignKey(x => x.EventTypeCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasData(
                new PaymentOutcomeCodeMapping { ResponseCode = "00", TransactionStatusContext = "Success", Origin = "AcmeStub", Meaning = "Approved", PaymentStatusCode = PaymentStatus.Approved, EventTypeCode = PaymentEventType.Approved },
                new PaymentOutcomeCodeMapping { ResponseCode = "00", TransactionStatusContext = "Complete", Origin = "AcmeStub", Meaning = "Approved EFT collection", PaymentStatusCode = PaymentStatus.Approved, EventTypeCode = PaymentEventType.Approved },
                new PaymentOutcomeCodeMapping { ResponseCode = "00", TransactionStatusContext = "Reversed", Origin = "AcmeStub", Meaning = "Payment reversed", PaymentStatusCode = PaymentStatus.Reversed, EventTypeCode = PaymentEventType.Reversed },
                new PaymentOutcomeCodeMapping { ResponseCode = "00", TransactionStatusContext = "Refunded", Origin = "AcmeStub", Meaning = "Payment refunded", PaymentStatusCode = PaymentStatus.Refunded, EventTypeCode = PaymentEventType.RefundApplied },
                new PaymentOutcomeCodeMapping { ResponseCode = "05", TransactionStatusContext = "Success", Origin = "AcmeStub", Meaning = "Do not honour", PaymentStatusCode = PaymentStatus.Declined, EventTypeCode = PaymentEventType.Declined },
                new PaymentOutcomeCodeMapping { ResponseCode = "51", TransactionStatusContext = "Success", Origin = "AcmeStub", Meaning = "Insufficient funds", PaymentStatusCode = PaymentStatus.Declined, EventTypeCode = PaymentEventType.Declined },
                new PaymentOutcomeCodeMapping { ResponseCode = "54", TransactionStatusContext = "Success", Origin = "AcmeStub", Meaning = "Expired card", PaymentStatusCode = PaymentStatus.Declined, EventTypeCode = PaymentEventType.Declined },
                new PaymentOutcomeCodeMapping { ResponseCode = "91", TransactionStatusContext = "Success", Origin = "AcmeStub", Meaning = "Issuer or switch unavailable", PaymentStatusCode = PaymentStatus.Declined, EventTypeCode = PaymentEventType.Declined },
                new PaymentOutcomeCodeMapping { ResponseCode = "PEND", TransactionStatusContext = "Processing", Origin = "AcmeStub", Meaning = "Transaction is pending", PaymentStatusCode = PaymentStatus.Processing, EventTypeCode = PaymentEventType.Processing },
                new PaymentOutcomeCodeMapping { ResponseCode = "COMPL", TransactionStatusContext = "Complete", Origin = "AcmeStub", Meaning = "EFT collection completed", PaymentStatusCode = PaymentStatus.Approved, EventTypeCode = PaymentEventType.Approved },
                new PaymentOutcomeCodeMapping { ResponseCode = "E01", TransactionStatusContext = "Failure", Origin = "AcmeStub", Meaning = "Invalid request", PaymentStatusCode = PaymentStatus.SubmissionFailed, EventTypeCode = PaymentEventType.SubmissionFailed },
                new PaymentOutcomeCodeMapping { ResponseCode = "E02", TransactionStatusContext = "Failure", Origin = "AcmeStub", Meaning = "Unauthorized request", PaymentStatusCode = PaymentStatus.SubmissionFailed, EventTypeCode = PaymentEventType.SubmissionFailed },
                new PaymentOutcomeCodeMapping { ResponseCode = "E09", TransactionStatusContext = "Failure", Origin = "AcmeStub", Meaning = "Duplicate transaction or retrieval reference conflict", PaymentStatusCode = PaymentStatus.SubmissionFailed, EventTypeCode = PaymentEventType.SubmissionFailed },
                new PaymentOutcomeCodeMapping { ResponseCode = "E29", TransactionStatusContext = "Failure", Origin = "AcmeStub", Meaning = "Gateway request limit exceeded", PaymentStatusCode = PaymentStatus.SubmissionFailed, EventTypeCode = PaymentEventType.SubmissionFailed },
                new PaymentOutcomeCodeMapping { ResponseCode = "E50", TransactionStatusContext = "Failure", Origin = "AcmeStub", Meaning = "Gateway temporarily unavailable", PaymentStatusCode = PaymentStatus.SubmissionFailed, EventTypeCode = PaymentEventType.SubmissionFailed },
                new PaymentOutcomeCodeMapping { ResponseCode = "TIMEOUT", TransactionStatusContext = "(none)", Origin = "Zapper", Meaning = "Provider outcome is unknown after a timeout", PaymentStatusCode = PaymentStatus.OutcomeUnknown, EventTypeCode = PaymentEventType.OutcomeUnknown },
                new PaymentOutcomeCodeMapping { ResponseCode = "TRANSPORT_UNKNOWN", TransactionStatusContext = "(none)", Origin = "Zapper", Meaning = "Provider outcome is unknown after a transport failure", PaymentStatusCode = PaymentStatus.OutcomeUnknown, EventTypeCode = PaymentEventType.OutcomeUnknown });
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.AcmeTransactionId).HasMaxLength(50).IsRequired();
            entity.Property(x => x.AcmeOrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RetrievalReferenceNumber).HasMaxLength(12);
            entity.Property(x => x.AcmeResponseCode).HasMaxLength(20);
            entity.Property(x => x.AcmeResponseMessageId).HasMaxLength(100);
            entity.Property(x => x.AcmeTransactionStatus).HasMaxLength(50);
            entity.Property(x => x.AcmeResponseSource).HasMaxLength(50);
            entity.Property(x => x.AcmeResponseDescription).HasMaxLength(1000);
            entity.Property(x => x.AcmeClientMessage).HasMaxLength(1000);
            entity.Property(x => x.AcmeReconId).HasMaxLength(100);
            entity.Property(x => x.AcmeAuthCode).HasMaxLength(50);
            entity.Property(x => x.AcmeOtherTransactionId).HasMaxLength(50);
            entity.Property(x => x.AcmeResponsePayload).HasColumnType("text");
            entity.HasIndex(x => x.AppPaymentId).IsUnique();
            entity.HasIndex(x => x.AcmeTransactionId).IsUnique();
            entity.HasIndex(x => x.RetrievalReferenceNumber).IsUnique();
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.HasIndex(x => new { x.SubMerchantId, x.CreatedAt });
            entity.HasOne<PaymentStatusLookup>()
                .WithMany()
                .HasForeignKey(x => x.Status)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.LifecycleEvents).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentLifecycleEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalEventId).HasMaxLength(200);
            entity.Property(x => x.ResponseCode).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PaymentId, x.ExternalEventId }).IsUnique();
            entity.HasIndex(x => new { x.OccurredAt, x.Type });
            entity.HasOne<PaymentLifecycleEventTypeLookup>()
                .WithMany()
                .HasForeignKey(x => x.Type)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InboundNotification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceName)
                .HasComputedColumnSql("CASE \"Source\" WHEN 0 THEN 'PaymentAdvice' WHEN 1 THEN 'TransactionStatus' ELSE 'Unknown' END", stored: true);
            entity.Property(x => x.ProcessingStatusName)
                .HasComputedColumnSql("CASE \"ProcessingStatus\" WHEN 0 THEN 'Received' WHEN 1 THEN 'Processed' WHEN 2 THEN 'IgnoredAsDuplicate' WHEN 3 THEN 'Rejected' WHEN 4 THEN 'Unmatched' ELSE 'Unknown' END", stored: true);
            entity.Property(x => x.DeduplicationKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ExternalEventId).HasMaxLength(200);
            entity.Property(x => x.AcmeTransactionId).HasMaxLength(50);
            entity.Property(x => x.RetrievalReferenceNumber).HasMaxLength(12);
            entity.Property(x => x.PayloadSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RawPayload).IsRequired();
            entity.Property(x => x.ProcessingNote).HasMaxLength(1000);
            entity.HasIndex(x => new { x.Source, x.DeduplicationKey });
            entity.HasIndex(x => new { x.Source, x.ExternalEventId });
            entity.HasIndex(x => x.AcmeTransactionId);
            entity.HasIndex(x => x.ReceivedAt);
        });
    }
}

internal sealed class EfUnitOfWorkTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
