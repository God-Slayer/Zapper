using Microsoft.Extensions.Logging;
using System.Net;
using Zapper.Application.Abstractions;
using Zapper.Application.AcmePay;
using Zapper.Application.Time;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Application.Payments;

public sealed class PaymentService(
    ICustomerRepository customerRepository,
    ISubMerchantRepository subMerchantRepository,
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    IAcmePayClient acmePayClient,
    PaymentServiceOptions options,
    ILogger<PaymentService> logger) : IPaymentService
{
    private const int MaxReservationAttempts = 5;

    public async Task<SubmitPaymentResult> SubmitAsync(SubmitPaymentCommand command, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Payment" });
        Validate(command);
        var currency = string.IsNullOrWhiteSpace(command.CurrencyCode) ? "ZAR" : command.CurrencyCode.Trim().ToUpperInvariant();
        var existing = await paymentRepository.GetByAppPaymentIdAsync(command.AppPaymentId, cancellationToken);
        if (existing is not null)
        {
            var replayMerchant = await subMerchantRepository.GetByZapperMerchantIdAsync(command.ZapperMerchantId.Trim(), cancellationToken);
            var replay = ReplayOrConflict(existing, command, currency, replayMerchant?.Id);
            if (existing.Status == PaymentStatus.SubmissionFailed && existing.AcmeResponseCode == "E29")
            {
                var retryCustomer = await customerRepository.GetWithStoredCardsAsync(existing.CustomerId, cancellationToken)
                    ?? throw new PaymentApplicationException("CUSTOMER_NOT_FOUND", "Customer was not found.", (int)HttpStatusCode.NotFound);
                var retryMerchant = replayMerchant
                    ?? throw new PaymentApplicationException("MERCHANT_NOT_FOUND", "Sub-merchant was not found.", (int)HttpStatusCode.NotFound);
                var retryCardRequired = options.PaymentService is "CardNotPresent" or "CardNotPresentRecurring" or "GooglePay" or "ApplePay";
                var retryCard = retryCardRequired ? retryCustomer.StoredCards.OrderBy(x => x.Id).FirstOrDefault() : null;
                if (retryCardRequired && retryCard is null)
                    throw new PaymentApplicationException("SAVED_CARD_NOT_FOUND", "Customer has no stored card token.", (int)HttpStatusCode.UnprocessableEntity);
                logger.LogInformation("Retrying previously throttled AppPaymentId {AppPaymentId} with its original Acme transaction identifiers.", existing.AppPaymentId);
                return await SendReservedPaymentAsync(existing,
                    BuildRequest(existing, retryMerchant, retryCustomer.EmailAddress, retryCard?.AcmeToken), cancellationToken);
            }
            logger.LogDebug("Returning payment replay for {AppPaymentId} with status {PaymentStatus}.", command.AppPaymentId, existing.Status);
            return replay;
        }

        var customer = await customerRepository.GetWithStoredCardsAsync(command.CustomerId, cancellationToken)
            ?? throw new PaymentApplicationException("CUSTOMER_NOT_FOUND", "Customer was not found.", (int)HttpStatusCode.NotFound);
        var requiresCard = options.PaymentService is "CardNotPresent" or "CardNotPresentRecurring" or "GooglePay" or "ApplePay";
        var card = requiresCard ? customer.StoredCards.OrderBy(x => x.Id).FirstOrDefault() : null;
        if (requiresCard && card is null)
            throw new PaymentApplicationException("SAVED_CARD_NOT_FOUND", "Customer has no stored card token.", (int)HttpStatusCode.UnprocessableEntity);
        var merchant = await subMerchantRepository.GetByZapperMerchantIdAsync(command.ZapperMerchantId.Trim(), cancellationToken)
            ?? throw new PaymentApplicationException("MERCHANT_NOT_FOUND", "Sub-merchant was not found.", (int)HttpStatusCode.NotFound);

        Payment? payment = null;
        AcmePaymentRequestEnvelope? request = null;
        for (var attempt = 0; attempt < MaxReservationAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var rrn = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var orderNumber = rrn + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            if (await paymentRepository.GetByRetrievalReferenceNumberAsync(rrn, cancellationToken) is not null)
                continue;

            payment = new Payment
            {
                Id = Guid.NewGuid(),
                AppPaymentId = command.AppPaymentId,
                CustomerId = customer.Id,
                SubMerchantId = merchant.Id,
                AmountMinorUnits = command.AmountMinorUnits,
                CurrencyCode = currency,
                Status = PaymentStatus.Pending,
                AcmeTransactionId = Guid.NewGuid().ToString(),
                AcmeOrderNumber = orderNumber,
                RetrievalReferenceNumber = rrn,
                CreatedAt = now,
                UpdatedAt = now
            };
            payment.LifecycleEvents.Add(new PaymentLifecycleEvent
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                Type = PaymentEventType.Submitted,
                AmountMinorUnits = command.AmountMinorUnits,
                CurrencyCode = currency,
                OccurredAt = now,
                RecordedAt = now,
                Source = "ZapperApi"
            });
            request = BuildRequest(payment, merchant, customer.EmailAddress, card?.AcmeToken);
            if (await paymentRepository.TryAddAsync(payment, cancellationToken)) break;

            var concurrentReplay = await paymentRepository.GetByAppPaymentIdAsync(command.AppPaymentId, cancellationToken);
            if (concurrentReplay is not null) return ReplayOrConflict(concurrentReplay, command, currency, merchant.Id);
            payment = null;
        }

        if (payment is null || request is null)
            throw new PaymentApplicationException("PAYMENT_RESERVATION_FAILED", "Could not reserve a unique payment identifier. Please retry with the same AppPaymentId.", (int)HttpStatusCode.ServiceUnavailable);

        return await SendReservedPaymentAsync(payment, request, cancellationToken);
    }

    private async Task<SubmitPaymentResult> SendReservedPaymentAsync(Payment payment, AcmePaymentRequestEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            using var paymentScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["AppPaymentId"] = payment.AppPaymentId,
                ["AcmeTransactionId"] = payment.AcmeTransactionId,
                ["AcmeMessageId"] = request.MessageHeader.MessageID
            });
            logger.LogInformation("Submitting payment to Acme with {AcmeMessageId}.", request.MessageHeader.MessageID);
            var response = await acmePayClient.SubmitPaymentAsync(request, cancellationToken);
            var lifecycleEvent = ApplyResponse(payment, response, "AcmeResponse");
            logger.LogInformation("Acme returned {AcmeTransactionStatus} with response code {AcmeResponseCode}.", payment.AcmeTransactionStatus, payment.AcmeResponseCode);
            await paymentRepository.AddLifecycleEventAsync(lifecycleEvent, CancellationToken.None);
            await SavePaymentChangesAsync(payment, "recording Acme's payment response");
            return new(ToResult(payment), false);
        }
        catch (OperationCanceledException)
        {
            // An HTTP timeout does not prove that Acme did not process the payment. Reconcile with
            // the already persisted identifiers; never issue a fresh TransactionID for this AppPaymentId.
            AcmePaymentResponseEnvelope? query = null;
            try
            {
                using var reconciliationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                logger.LogWarning("Acme submission was cancelled or timed out; querying the original transaction {AcmeTransactionId}.", payment.AcmeTransactionId);
                query = await acmePayClient.QueryTransactionAsync(options.AcmeMerchantId, payment.AcmeTransactionId, payment.AcmeOrderNumber, reconciliationTimeout.Token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Acme reconciliation failed after the payment submission timed out for transaction {AcmeTransactionId}; the outcome remains uncertain.",
                    payment.AcmeTransactionId);
            }

            if (query?.PaymentResponse is not null)
            {
                await paymentRepository.AddLifecycleEventAsync(ApplyResponse(payment, query, "AcmeReconciliation"), CancellationToken.None);
                await SavePaymentChangesAsync(payment, "recording Acme's reconciled payment response");
                return new(ToResult(payment), false);
            }

            await paymentRepository.AddLifecycleEventAsync(ApplyUnresolved(payment, PaymentStatus.OutcomeUnknown, "Acme request timed out; query required.", "TIMEOUT"), CancellationToken.None);
            await SavePaymentChangesAsync(payment, "recording an unresolved payment outcome after timeout");
            return new(ToResult(payment), false);
        }
        catch (AcmePayException ex)
        {
            payment.Status = PaymentStatus.SubmissionFailed;
            payment.AcmeResponseCode = ex.Code;
            payment.AcmeResponseMessageId = ex.ResponseMessageId;
            payment.AcmeTransactionStatus = ex.TransactionStatus ?? "Failure";
            payment.AcmeResponseSource = ex.ResponseSource;
            payment.AcmeResponseDescription = ex.Message;
            payment.AcmeClientMessage = ex.ClientMessage;
            payment.AcmeReconId = ex.ReconId;
            payment.AcmeAuthCode = ex.AuthCode;
            payment.AcmeOtherTransactionId = ex.OtherTransactionId;
            payment.AcmeResponsePayload = ex.RawPayload;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            payment.LastPaymentOutcomeAt = payment.UpdatedAt;
            await paymentRepository.AddLifecycleEventAsync(NewEvent(payment, PaymentEventType.SubmissionFailed, ex.Code, ex.Message, "AcmeError"), CancellationToken.None);
            await SavePaymentChangesAsync(payment, "recording Acme's failed-submission response");
            logger.LogWarning("Acme rejected payment {AcmeTransactionId} with code {AcmeResponseCode} and HTTP status {UpstreamStatusCode}.", payment.AcmeTransactionId, ex.Code, ex.StatusCode);
            var outwardStatus = ex.Code == "E09" ? (int)HttpStatusCode.Conflict : ex.StatusCode == (int)HttpStatusCode.TooManyRequests ? (int)HttpStatusCode.ServiceUnavailable : (int)HttpStatusCode.BadGateway;
            return new(ToResult(payment), false, outwardStatus, ex.RetryAfter);
        }
        catch (HttpRequestException ex)
        {
            await paymentRepository.AddLifecycleEventAsync(ApplyUnresolved(payment, PaymentStatus.OutcomeUnknown, "Acme transport outcome is unknown; query required.", "TRANSPORT_UNKNOWN"), CancellationToken.None);
            await SavePaymentChangesAsync(payment, "recording an unknown outcome after an Acme transport failure");
            logger.LogWarning(ex,
                "Acme transport failed for transaction {AcmeTransactionId}; the payment outcome is unknown and requires reconciliation.",
                payment.AcmeTransactionId);
            return new(ToResult(payment), false, (int)HttpStatusCode.ServiceUnavailable);
        }
    }

    public async Task<PaymentStatusResult?> GetByAppPaymentIdAsync(Guid appPaymentId, CancellationToken cancellationToken)
    {
        using var categoryScope = logger.BeginScope(new Dictionary<string, object> { ["LogCategory"] = "Payment" });
        var payment = await paymentRepository.GetByAppPaymentIdAsync(appPaymentId, cancellationToken);
        if (payment is null) return null;
        if (payment.Status is PaymentStatus.Processing or PaymentStatus.OutcomeUnknown)
        {
            AcmePaymentResponseEnvelope? query = null;
            try
            {
                query = await acmePayClient.QueryTransactionAsync(options.AcmeMerchantId, payment.AcmeTransactionId, payment.AcmeOrderNumber, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Return the last durable state; a later GET can reconcile again.
                logger.LogWarning(ex,
                    "Acme reconciliation failed while checking AppPaymentId {AppPaymentId} and transaction {AcmeTransactionId}; returning the last saved payment status.",
                    payment.AppPaymentId, payment.AcmeTransactionId);
            }

            if (query?.PaymentResponse is not null)
            {
                var latestLifecycleEvent = payment.LifecycleEvents
                    .OrderByDescending(existingEvent => existingEvent.RecordedAt)
                    .FirstOrDefault();
                var lifecycleEvent = ApplyResponse(payment, query, "AcmeReconciliation");
                var isDuplicate = lifecycleEvent.ExternalEventId is not null &&
                    payment.LifecycleEvents.Any(existingEvent => existingEvent.ExternalEventId == lifecycleEvent.ExternalEventId)
                    || latestLifecycleEvent is not null && IsSameOutcome(latestLifecycleEvent, lifecycleEvent);
                if (!isDuplicate)
                    await paymentRepository.AddLifecycleEventAsync(lifecycleEvent, CancellationToken.None);
                else
                    logger.LogDebug("Skipping repeated reconciliation outcome for AppPaymentId {AppPaymentId} with response code {AcmeResponseCode}.", payment.AppPaymentId, lifecycleEvent.ResponseCode);
                await SavePaymentChangesAsync(payment, "recording a reconciled payment response");
            }
            else
                logger.LogDebug("Acme has no reconciliation result yet for AppPaymentId {AppPaymentId}; retaining status {PaymentStatus}.", payment.AppPaymentId, payment.Status);
        }
        return ToResult(payment);
    }

    private async Task SavePaymentChangesAsync(Payment payment, string operation)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to persist AppPaymentId {AppPaymentId} and Acme transaction {AcmeTransactionId} while {Operation}.",
                payment.AppPaymentId, payment.AcmeTransactionId, operation);
            throw;
        }
    }

    private static SubmitPaymentResult ReplayOrConflict(Payment existing, SubmitPaymentCommand command, string currency, Guid? subMerchantId)
    {
        if (existing.CustomerId != command.CustomerId || existing.AmountMinorUnits != command.AmountMinorUnits ||
            existing.CurrencyCode != currency || subMerchantId != existing.SubMerchantId)
            throw new PaymentApplicationException("IDEMPOTENCY_CONFLICT", "AppPaymentId was already used with a different payment request.", (int)HttpStatusCode.Conflict);
        return new(ToResult(existing), true);
    }

    private AcmePaymentRequestEnvelope BuildRequest(Payment payment, SubMerchant merchant, string email, string? token) => new()
    {
        MessageHeader = new AcmeMessageHeader
        {
            MessageDateTime = FormatSouthAfricanTime(DateTimeOffset.UtcNow),
            MessageID = Guid.NewGuid().ToString(),
            Channel = options.Channel
        },
        PaymentRequest = new AcmePaymentRequest
        {
            MerchantID = options.AcmeMerchantId,
            TransactionID = payment.AcmeTransactionId,
            OrderNumber = payment.AcmeOrderNumber,
            Amount = payment.AmountMinorUnits,
            CurrencyCode = payment.CurrencyCode,
            PaymentService = options.PaymentService,
            Card = token is null ? null : new AcmeCard { Token = token },
            CardOnFileScenario = token is null ? null : options.CardOnFileScenario,
            SubMerchantID = merchant.ZapperMerchantId,
            MerchantCategoryCode = merchant.MerchantCategoryCode,
            CardAcceptorName = FormatCardAcceptorName(merchant, options.FacilitatorName),
            Email = email
        }
    };

    private static PaymentLifecycleEvent ApplyResponse(Payment payment, AcmePaymentResponseEnvelope envelope, string source)
    {
        var response = envelope.PaymentResponse;
        var now = DateTimeOffset.UtcNow;
        var occurredAt = (ParseProviderDate(response.TransactionDateTime) ?? now).ToUniversalTime();
        payment.RetrievalReferenceNumber = response.RetrievalReferenceNumber ?? payment.RetrievalReferenceNumber;
        if (response.AuthAmount.HasValue)
            payment.AuthorizedAmountMinorUnits = response.AuthAmount;
        payment.AcmeResponseCode = response.ResponseDetail?.Code;
        payment.AcmeResponseMessageId = envelope.MessageHeader.MessageID;
        payment.AcmeTransactionStatus = response.TransactionStatus;
        payment.AcmeResponseSource = response.ResponseDetail?.Source;
        payment.AcmeResponseDescription = response.ResponseDetail?.Description;
        payment.AcmeClientMessage = response.ResponseDetail?.ClientMessage;
        payment.AcmeReconId = response.ReconID;
        payment.AcmeAuthCode = response.AuthCode;
        payment.AcmeResponsePayload = envelope.RawPayload;
        payment.UpdatedAt = now;
        payment.LastPaymentOutcomeAt = occurredAt;

        var status = response.TransactionStatus?.Trim().ToUpperInvariant();
        var code = response.ResponseDetail?.Code?.Trim().ToUpperInvariant();
        var type = code == "PEND" || status == "PROCESSING" ? PaymentEventType.Processing : status switch
        {
            "SUCCESS" when code is "00" or "COMPL" => PaymentEventType.Approved,
            "COMPLETE" when code is "00" or "COMPL" => PaymentEventType.Approved,
            "SUCCESS" => PaymentEventType.Declined,
            "FAILURE" => PaymentEventType.SubmissionFailed,
            "REVERSED" => PaymentEventType.Reversed,
            "REFUNDED" => PaymentEventType.RefundApplied,
            _ => PaymentEventType.OutcomeUnknown
        };
        payment.Status = type switch
        {
            PaymentEventType.Processing => PaymentStatus.Processing,
            PaymentEventType.Approved => PaymentStatus.Approved,
            PaymentEventType.Declined => PaymentStatus.Declined,
            PaymentEventType.SubmissionFailed => PaymentStatus.SubmissionFailed,
            PaymentEventType.Reversed => PaymentStatus.Reversed,
            PaymentEventType.RefundApplied => PaymentStatus.Refunded,
            _ => PaymentStatus.OutcomeUnknown
        };
        var amount = type switch
        {
            PaymentEventType.Approved => response.AuthAmount ?? payment.AmountMinorUnits,
            PaymentEventType.Reversed or PaymentEventType.RefundApplied => response.AuthAmount ?? payment.AuthorizedAmountMinorUnits ?? payment.AmountMinorUnits,
            _ => payment.AmountMinorUnits
        };
        return new PaymentLifecycleEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Type = type,
            AmountMinorUnits = amount,
            CurrencyCode = payment.CurrencyCode,
            OccurredAt = occurredAt,
            RecordedAt = now,
            Source = source,
            ExternalEventId = response.TransactionID is null ? null : $"{source}:{response.TransactionID}:{response.TransactionStatus}:{response.ResponseDetail?.Code}",
            ResponseCode = response.ResponseDetail?.Code,
            Description = response.ResponseDetail?.Description
        };
    }

    private static bool IsSameOutcome(PaymentLifecycleEvent previous, PaymentLifecycleEvent current) =>
        previous.Type == current.Type &&
        string.Equals(previous.ResponseCode, current.ResponseCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(previous.Description, current.Description, StringComparison.Ordinal) &&
        previous.AmountMinorUnits == current.AmountMinorUnits &&
        string.Equals(previous.CurrencyCode, current.CurrencyCode, StringComparison.OrdinalIgnoreCase);

    private static PaymentLifecycleEvent ApplyUnresolved(Payment payment, PaymentStatus status, string description, string responseCode)
    {
        var now = DateTimeOffset.UtcNow;
        payment.Status = status;
        payment.AcmeResponseCode = responseCode;
        payment.UpdatedAt = now;
        return NewEvent(payment, status == PaymentStatus.OutcomeUnknown ? PaymentEventType.OutcomeUnknown : PaymentEventType.SubmissionFailed, responseCode, description, "ZapperApi");
    }

    private static PaymentLifecycleEvent NewEvent(Payment payment, PaymentEventType type, string? code, string description, string source) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = payment.Id,
        Type = type,
        AmountMinorUnits = payment.AmountMinorUnits,
        CurrencyCode = payment.CurrencyCode,
        OccurredAt = DateTimeOffset.UtcNow,
        RecordedAt = DateTimeOffset.UtcNow,
        Source = source,
        ExternalEventId = null,
        ResponseCode = code,
        Description = description
    };

    private static PaymentStatusResult ToResult(Payment payment) => new(
        payment.Id, payment.AppPaymentId, payment.Status, payment.AmountMinorUnits,
        payment.CurrencyCode, payment.AuthorizedAmountMinorUnits, payment.AcmeResponseCode,
        payment.CreatedAt, payment.UpdatedAt, payment.AcmeResponseMessageId, payment.AcmeTransactionStatus,
        payment.AcmeResponseSource, payment.AcmeResponseDescription, payment.AcmeClientMessage,
        payment.AcmeReconId, payment.AcmeAuthCode, payment.AcmeOtherTransactionId);

    private static void Validate(SubmitPaymentCommand command)
    {
        if (command.CustomerId == Guid.Empty) throw new PaymentApplicationException("INVALID_CUSTOMER_ID", "CustomerId must be a non-empty GUID.", (int)HttpStatusCode.BadRequest);
        if (command.AppPaymentId == Guid.Empty) throw new PaymentApplicationException("INVALID_APP_PAYMENT_ID", "AppPaymentId must be a non-empty GUID.", (int)HttpStatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(command.ZapperMerchantId) || command.ZapperMerchantId.Length > 50)
            throw new PaymentApplicationException("INVALID_MERCHANT_ID", "SubMerchantId is required and must not exceed 50 characters.", (int)HttpStatusCode.BadRequest);
        if (command.AmountMinorUnits <= 0) throw new PaymentApplicationException("INVALID_AMOUNT", "Amount must be a positive integer in minor currency units.", (int)HttpStatusCode.BadRequest);
        if (!string.IsNullOrWhiteSpace(command.CurrencyCode) && (command.CurrencyCode.Trim().Length != 3 || command.CurrencyCode.Trim().Any(c => !char.IsAsciiLetter(c))))
            throw new PaymentApplicationException("INVALID_CURRENCY", "CurrencyCode must be a three-letter ISO currency code.", (int)HttpStatusCode.BadRequest);
    }

    private static string FormatCardAcceptorName(SubMerchant merchant, string facilitatorName)
    {
        var merchantDescriptor = $"{facilitatorName}*{merchant.TradingName}";
        var separatorPosition = facilitatorName.Length + 1;
        if (separatorPosition is not (4 or 8 or 13)
            || merchantDescriptor.Length > 23
            || merchant.City.Length > 13
            || merchant.RegionCode.Length < 2
            || merchant.CountryCode.Length != 2)
            throw new PaymentApplicationException("INVALID_MERCHANT_DESCRIPTOR", "Merchant name/location fields do not fit the required 40-character card acceptor format.", (int)HttpStatusCode.UnprocessableEntity);
        return string.Concat(merchantDescriptor.PadRight(23), merchant.City.PadRight(13), merchant.RegionCode[..2], merchant.CountryCode);
    }

    private static string FormatSouthAfricanTime(DateTimeOffset value) => SouthAfricanTime.FormatProviderTimestamp(value);

    private static DateTimeOffset? ParseProviderDate(string? value) => SouthAfricanTime.ParseProviderTimestamp(value);
}

public sealed class PaymentServiceOptions
{
    public string AcmeMerchantId { get; init; } = "MERCHANT1";
    public string FacilitatorName { get; init; } = "ZAP";
    public string PaymentService { get; init; } = "CardNotPresent";
    public string CardOnFileScenario { get; init; } = "CIT";
    public string Channel { get; init; } = "App";
}
