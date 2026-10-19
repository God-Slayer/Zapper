using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Zapper.Application.Abstractions;
using Zapper.Application.AcmePay;
using Zapper.Application.Payments;
using Zapper.Application.Time;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Application.Notifications;

public sealed class NotificationApplicationService(
    IPaymentRepository paymentRepository,
    IInboundNotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    NotificationOptions options,
    ILogger<NotificationApplicationService> logger)
{
    public bool IsValidPaymentAdviceCredential(string? username, string? password) =>
        FixedTimeEquals(username, options.AdviceUsername) && FixedTimeEquals(password, options.AdvicePassword);

    public bool IsValidWebhookSignature(ReadOnlySpan<byte> payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(signature); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookSecret), payload);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    public async Task ProcessPaymentAdviceAsync(AcmePaymentAdviceNotification advice, string rawPayload, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Notification" });
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var requestType = advice.RequestType.Trim();
        var paymentService = advice.PaymentService.Trim();
        var transactionStatus = advice.TransactionStatus.Trim();
        var allowedServices = new[] { "CardNotPresent", "CardNotPresentRecurring", "AcmeEft", "GooglePay", "ApplePay" };
        if (string.IsNullOrWhiteSpace(advice.MerchantID) || advice.MerchantID.Length > 50 ||
            string.IsNullOrWhiteSpace(advice.TransactionID) || advice.TransactionID.Length > 50 ||
            advice.Amount <= 0 || requestType is not ("Authorize" or "Capture" or "Payment") ||
            string.IsNullOrWhiteSpace(advice.ResponseCode) || advice.ResponseCode.Length > 5 ||
            transactionStatus is not ("Success" or "Failure" or "Other") ||
            advice.RetrievalReferenceNumber.Length != 12 || string.IsNullOrWhiteSpace(advice.SystemsTraceAuditNumber) || advice.SystemsTraceAuditNumber.Length > 6 ||
            !allowedServices.Contains(paymentService, StringComparer.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(advice.ResponseSource) || advice.ResponseSource.Length > 50 ||
            !string.Equals(advice.CommProtocol, "REST", StringComparison.OrdinalIgnoreCase) ||
            advice.CurrencyCode is { Length: > 3 } || advice.CurrencyCode is not null && advice.CurrencyCode.Length != 3)
            throw InvalidPayload("Payment Advice is missing required fields or contains an invalid amount.");
        if (!string.Equals(advice.MerchantID, options.MerchantId, StringComparison.OrdinalIgnoreCase))
            throw InvalidPayload("Payment Advice MerchantID does not match the configured Acme merchant account.");

        var occurredAt = ParseProviderDate(advice.TransactionDateTime);
        var eventIdentity = string.Join('|', advice.TransactionID, advice.RequestType, advice.TransactionStatus,
            advice.ResponseCode, occurredAt?.ToString("O", CultureInfo.InvariantCulture), advice.OrderNumber,
            advice.RetrievalReferenceNumber, advice.SystemsTraceAuditNumber, advice.ReconID,
            advice.CurrencyCode, advice.ResponseSource, advice.Amount.ToString(CultureInfo.InvariantCulture));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventIdentity)));
        var notification = NewNotification(NotificationSource.PaymentAdvice, key, null, advice.TransactionID,
            advice.RetrievalReferenceNumber, occurredAt, rawPayload);
        await notificationRepository.AcquireDeduplicationLockAsync(NotificationSource.PaymentAdvice, key, cancellationToken);
        if (await notificationRepository.ExistsAsync(NotificationSource.PaymentAdvice, key, null, cancellationToken))
        {
            notification.ProcessingStatus = NotificationProcessingStatus.IgnoredAsDuplicate;
            notification.ProcessingNote = "Duplicate Payment Advice delivery; recorded without reapplying the event.";
            await notificationRepository.AddAsync(notification, cancellationToken);
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Ignored duplicate Payment Advice for Acme transaction {AcmeTransactionId}.", advice.TransactionID);
            return;
        }
        await notificationRepository.AddAsync(notification, cancellationToken);

        await paymentRepository.LockByAcmeTransactionIdAsync(advice.TransactionID, cancellationToken);
        var payment = await paymentRepository.GetByAcmeTransactionIdAsync(advice.TransactionID, cancellationToken)
            ?? await FindPaymentByRrnUnderLockAsync(advice.RetrievalReferenceNumber, cancellationToken);
        if (payment is null)
        {
            notification.ProcessingStatus = NotificationProcessingStatus.Unmatched;
            notification.ProcessingNote = "No local payment matched the Acme TransactionID or RetrievalReferenceNumber.";
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Payment Advice for Acme transaction {AcmeTransactionId} and RRN {RetrievalReferenceNumber} did not match a local payment.", advice.TransactionID, advice.RetrievalReferenceNumber);
            return;
        }

        var status = transactionStatus.ToUpperInvariant();
        var normalizedCode = advice.ResponseCode.Trim().ToUpperInvariant();
        var isOutcome = status is "SUCCESS" or "FAILURE";
        var isPending = normalizedCode == "PEND";
        var isNewer = occurredAt is null
            ? payment.LastPaymentOutcomeAt is null && payment.Status is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.OutcomeUnknown
            : payment.LastPaymentOutcomeAt is null || occurredAt > payment.LastPaymentOutcomeAt ||
                occurredAt == payment.LastPaymentOutcomeAt && payment.Status is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.OutcomeUnknown;
        if ((!isOutcome && !isPending) || !isNewer)
        {
            notification.ProcessingStatus = NotificationProcessingStatus.Processed;
            notification.ProcessingNote = !isOutcome && !isPending
                ? "Recorded non-terminal Payment Advice; no payment state transition applied."
                : !isNewer && occurredAt is null
                    ? "Recorded Payment Advice without an ordering timestamp; existing payment outcome retained."
                    : "Recorded stale Payment Advice; newer payment outcome retained.";
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Payment Advice for AppPaymentId {AppPaymentId} was recorded without changing payment status: {ProcessingNote}.", payment.AppPaymentId, notification.ProcessingNote);
            return;
        }

        var type = status switch
        {
            _ when isPending => PaymentEventType.Processing,
            "SUCCESS" when normalizedCode is "00" or "COMPL" => PaymentEventType.Approved,
            "SUCCESS" => PaymentEventType.Declined,
            "FAILURE" => PaymentEventType.SubmissionFailed,
            _ => PaymentEventType.OutcomeUnknown
        };

        payment.Status = type switch
        {
            PaymentEventType.Approved => PaymentStatus.Approved,
            PaymentEventType.Declined => PaymentStatus.Declined,
            PaymentEventType.Processing => PaymentStatus.Processing,
            PaymentEventType.SubmissionFailed => PaymentStatus.SubmissionFailed,
            PaymentEventType.Reversed => PaymentStatus.Reversed,
            _ => PaymentStatus.OutcomeUnknown
        };
        payment.AcmeResponseCode = advice.ResponseCode;
        payment.AcmeTransactionStatus = transactionStatus;
        payment.AcmeResponseSource = advice.ResponseSource;
        payment.AcmeResponseDescription = advice.ResponseDescription ?? transactionStatus;
        payment.AcmeReconId = advice.ReconID;
        payment.AcmeAuthCode = advice.AuthCode;
        payment.AcmeResponsePayload = rawPayload;
        payment.RetrievalReferenceNumber = string.IsNullOrWhiteSpace(advice.RetrievalReferenceNumber) ? payment.RetrievalReferenceNumber : advice.RetrievalReferenceNumber;
        payment.AuthorizedAmountMinorUnits = type == PaymentEventType.Approved ? advice.Amount : payment.AuthorizedAmountMinorUnits;
        var providerTime = occurredAt ?? DateTimeOffset.UtcNow;
        if (occurredAt is not null)
            payment.LastPaymentOutcomeAt = occurredAt;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await paymentRepository.AddLifecycleEventAsync(new PaymentLifecycleEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Type = type,
            AmountMinorUnits = type is PaymentEventType.Approved or PaymentEventType.Reversed ? advice.Amount : payment.AmountMinorUnits,
            CurrencyCode = string.IsNullOrWhiteSpace(advice.CurrencyCode) ? payment.CurrencyCode : advice.CurrencyCode.ToUpperInvariant(),
            OccurredAt = providerTime,
            RecordedAt = DateTimeOffset.UtcNow,
            Source = "PaymentAdvice",
            ExternalEventId = $"PaymentAdvice:{key}",
            ResponseCode = advice.ResponseCode,
            Description = advice.ResponseDescription ?? advice.TransactionStatus
        }, cancellationToken);
        notification.ProcessingStatus = NotificationProcessingStatus.Processed;
        notification.ProcessingNote = "Payment Advice applied.";
        await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
    }

    public async Task ProcessTransactionStatusAsync(AcmeTransactionStatusNotification status, string rawPayload, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Notification" });
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (!Guid.TryParse(status.TransactionUUID, out var eventId) || string.IsNullOrWhiteSpace(status.TransactionType) ||
            string.IsNullOrWhiteSpace(status.RetrievalReferenceNumber) || status.TransactionAmount <= 0 || string.IsNullOrWhiteSpace(status.CurrencyCode))
            throw InvalidPayload("Transaction-status notification is missing required fields or contains an invalid transaction UUID/amount.");
        if (string.IsNullOrWhiteSpace(status.TransactionResultCode))
            throw InvalidPayload("Transaction-status notification requires txResultCode.");

        var occurredAt = ParseProviderDate(status.TransactionDateTime);
        var externalEventId = eventId.ToString("D");
        var notification = NewNotification(NotificationSource.TransactionStatus,
            externalEventId, externalEventId, null, status.RetrievalReferenceNumber, occurredAt, rawPayload);
        await notificationRepository.AcquireDeduplicationLockAsync(NotificationSource.TransactionStatus, externalEventId, cancellationToken);
        if (await notificationRepository.ExistsAsync(NotificationSource.TransactionStatus, externalEventId, externalEventId, cancellationToken))
        {
            notification.ProcessingStatus = NotificationProcessingStatus.IgnoredAsDuplicate;
            notification.ProcessingNote = "Duplicate transaction-status delivery; recorded without reapplying the event.";
            await notificationRepository.AddAsync(notification, cancellationToken);
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Ignored duplicate Acme transaction-status event {AcmeEventId}.", externalEventId);
            return;
        }
        await notificationRepository.AddAsync(notification, cancellationToken);

        await paymentRepository.LockByRetrievalReferenceNumberAsync(status.RetrievalReferenceNumber, cancellationToken);
        var payment = await paymentRepository.GetByRetrievalReferenceNumberAsync(status.RetrievalReferenceNumber, cancellationToken);
        if (payment is null)
        {
            notification.ProcessingStatus = NotificationProcessingStatus.Unmatched;
            notification.ProcessingNote = "No local payment matched the Retrieval Reference Number.";
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Acme transaction-status event {AcmeEventId} with RRN {RetrievalReferenceNumber} did not match a local payment.", externalEventId, status.RetrievalReferenceNumber);
            return;
        }

        var typeName = status.TransactionType.Trim();
        var type = typeName.Equals("Reversal", StringComparison.OrdinalIgnoreCase) ? PaymentEventType.Reversed
            : typeName.Equals("Refund", StringComparison.OrdinalIgnoreCase) ? PaymentEventType.RefundApplied
            : (PaymentEventType?)null;
        if (type is null)
        {
            notification.ProcessingStatus = NotificationProcessingStatus.Rejected;
            notification.ProcessingNote = $"Unsupported transaction type '{typeName}'.";
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Rejected transaction-status event {AcmeEventId} with unsupported transaction type {TransactionType}.", externalEventId, typeName);
            return;
        }

        if (status.IsApproved && status.TransactionResultCode == "00" &&
            !string.Equals(status.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            notification.ProcessingStatus = NotificationProcessingStatus.Rejected;
            notification.ProcessingNote = "Transaction-status currency does not match the original payment; state unchanged.";
            await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
            logger.LogDebug("Rejected transaction-status event {AcmeEventId} because its currency did not match AppPaymentId {AppPaymentId}.", externalEventId, payment.AppPaymentId);
            return;
        }

        if (status.IsApproved && status.TransactionResultCode == "00")
        {
            if (payment.Status is not (PaymentStatus.Approved or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded or PaymentStatus.Reversed))
            {
                notification.ProcessingStatus = NotificationProcessingStatus.Rejected;
                notification.ProcessingNote = "Applied reversal/refund requires an approved original payment.";
                await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
                logger.LogDebug("Rejected transaction-status event {AcmeEventId} because AppPaymentId {AppPaymentId} has no approved payment to adjust.", externalEventId, payment.AppPaymentId);
                return;
            }

            var previousRefunds = payment.LifecycleEvents.Where(x => x.Type == PaymentEventType.RefundApplied).Sum(x => x.AmountMinorUnits ?? 0);
            var priorReversal = payment.LifecycleEvents.Any(x => x.Type == PaymentEventType.Reversed);
            var authorizedAmount = payment.AuthorizedAmountMinorUnits ?? payment.AmountMinorUnits;
            if (type == PaymentEventType.RefundApplied && (priorReversal || status.TransactionAmount > authorizedAmount - previousRefunds) ||
                type == PaymentEventType.Reversed && (previousRefunds > 0 || priorReversal || status.TransactionAmount != authorizedAmount))
            {
                notification.ProcessingStatus = NotificationProcessingStatus.Rejected;
                notification.ProcessingNote = "Reversal/refund amount exceeds the remaining approved amount or conflicts with prior applied events.";
                await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
                logger.LogDebug("Rejected transaction-status event {AcmeEventId} because its amount conflicts with the remaining approved balance for AppPaymentId {AppPaymentId}.", externalEventId, payment.AppPaymentId);
                return;
            }

            var occurred = occurredAt ?? DateTimeOffset.UtcNow;
            await paymentRepository.AddLifecycleEventAsync(new PaymentLifecycleEvent
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                Type = type.Value,
                AmountMinorUnits = status.TransactionAmount,
                CurrencyCode = status.CurrencyCode.ToUpperInvariant(),
                OccurredAt = occurred,
                RecordedAt = DateTimeOffset.UtcNow,
                Source = "TransactionStatusWebhook",
                ExternalEventId = $"TransactionStatus:{externalEventId}",
                ResponseCode = status.TransactionResultCode,
                Description = typeName
            }, cancellationToken);
            payment.Status = type == PaymentEventType.Reversed ? PaymentStatus.Reversed
                : previousRefunds + status.TransactionAmount >= authorizedAmount
                    ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            if (payment.LastPaymentOutcomeAt is null || occurred > payment.LastPaymentOutcomeAt)
                payment.LastPaymentOutcomeAt = occurred;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            notification.ProcessingNote = "Approved transaction-status event applied.";
        }
        else
        {
            notification.ProcessingNote = "Declined/unapplied transaction-status event recorded; payment state unchanged.";
            logger.LogDebug("Recorded declined transaction-status event {AcmeEventId} for AppPaymentId {AppPaymentId} without changing payment status.", externalEventId, payment.AppPaymentId);
        }

        notification.ProcessingStatus = NotificationProcessingStatus.Processed;
        await SaveNotificationChangesAsync(notification, transaction, cancellationToken);
    }

    private static InboundNotification NewNotification(NotificationSource source, string key, string? externalId,
        string? transactionId, string? rrn, DateTimeOffset? occurred, string rawPayload) => new()
        {
            Id = Guid.NewGuid(),
            Source = source,
            DeduplicationKey = key,
            ExternalEventId = externalId,
            AcmeTransactionId = transactionId,
            RetrievalReferenceNumber = rrn,
            ReceivedAt = DateTimeOffset.UtcNow,
            ProviderOccurredAt = occurred,
            PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload))),
            RawPayload = rawPayload
        };

    private static bool FixedTimeEquals(string? actual, string expected)
    {
        if (actual is null) return false;
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static DateTimeOffset? ParseProviderDate(string? value) => SouthAfricanTime.ParseProviderTimestamp(value);

    private async Task<Payment?> FindPaymentByRrnUnderLockAsync(string rrn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rrn)) return null;
        await paymentRepository.LockByRetrievalReferenceNumberAsync(rrn, cancellationToken);
        return await paymentRepository.GetByRetrievalReferenceNumberAsync(rrn, cancellationToken);
    }

    private async Task SaveNotificationChangesAsync(InboundNotification notification, IUnitOfWorkTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent delivery committed the same provider identity first. The save is atomic,
            // so neither this duplicate notification nor its lifecycle changes were persisted.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to persist inbound notification {NotificationId} from {NotificationSource} for Acme transaction {AcmeTransactionId} and RRN {RetrievalReferenceNumber}.",
                notification.Id, notification.Source, notification.AcmeTransactionId, notification.RetrievalReferenceNumber);
            throw;
        }
    }

    private static PaymentApplicationException InvalidPayload(string message) =>
        new("INVALID_NOTIFICATION", message, (int)HttpStatusCode.BadRequest);
}

public sealed class NotificationOptions
{
    public string AdviceUsername { get; init; } = "";
    public string AdvicePassword { get; init; } = "";
    public string WebhookSecret { get; init; } = "";
    public string MerchantId { get; init; } = "";
}
