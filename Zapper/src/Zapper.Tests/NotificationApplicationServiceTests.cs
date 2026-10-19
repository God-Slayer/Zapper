using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Zapper.Application.Abstractions;
using Zapper.Application.AcmePay;
using Zapper.Application.Notifications;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Tests;

public sealed class NotificationApplicationServiceTests
{
    private const string AdviceUser = "advice-user";
    private const string AdvicePassword = "advice-password";
    private const string WebhookSecret = "test-webhook-secret";
    private const string MerchantId = "MERCHANT1";

    [Fact]
    public void PaymentAdviceCredentials_AcceptOnlyConfiguredPair()
    {
        var service = CreateService();

        Assert.True(service.IsValidPaymentAdviceCredential(AdviceUser, AdvicePassword));
        Assert.False(service.IsValidPaymentAdviceCredential(AdviceUser, "wrong"));
        Assert.False(service.IsValidPaymentAdviceCredential(null, AdvicePassword));
    }

    [Fact]
    public void WebhookSignature_ValidatesExactPayloadBytesAndRejectsInvalidSignatures()
    {
        var service = CreateService();
        var payload = Encoding.UTF8.GetBytes("{\"transactionUUID\":\"event-1\"}");
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), payload));

        Assert.True(service.IsValidWebhookSignature(payload, signature));
        Assert.False(service.IsValidWebhookSignature(Encoding.UTF8.GetBytes(" {\"transactionUUID\":\"event-1\"}"), signature));
        Assert.False(service.IsValidWebhookSignature(payload, "not-base64!"));
        Assert.False(service.IsValidWebhookSignature(payload, null));
    }

    [Theory]
    [InlineData("COMPL", PaymentStatus.Approved, PaymentEventType.Approved)]
    [InlineData("PEND", PaymentStatus.Processing, PaymentEventType.Processing)]
    public async Task PaymentAdvice_AppliesDocumentedCompletionAndPendingCodes(string code, PaymentStatus expectedStatus, PaymentEventType expectedEvent)
    {
        var payment = ExistingPayment();
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.PaymentAdvice, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await fixture.Service.ProcessPaymentAdviceAsync(Advice(code), "{}", CancellationToken.None);

        Assert.Equal(expectedStatus, payment.Status);
        Assert.Contains(fixture.LifecycleEvents, x => x.Type == expectedEvent);
        Assert.Equal(code, payment.AcmeResponseCode);
    }

    [Fact]
    public async Task PaymentAdvice_RefreshesProviderDetailsWhenPendingPaymentCompletes()
    {
        var payment = ExistingPayment();
        payment.Status = PaymentStatus.Processing;
        payment.AcmeResponseCode = "PEND";
        payment.AcmeTransactionStatus = "Other";
        payment.AcmeResponseDescription = "EFT collection is pending";
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.PaymentAdvice, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var advice = Advice("00") with
        {
            ResponseDescription = "Approved",
            ResponseSource = "Acquirer",
            AuthCode = "A12345",
            ReconID = "recon-1"
        };
        const string rawPayload = "{\"ResponseCode\":\"00\"}";

        await fixture.Service.ProcessPaymentAdviceAsync(advice, rawPayload, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal("00", payment.AcmeResponseCode);
        Assert.Equal("Success", payment.AcmeTransactionStatus);
        Assert.Equal("Acquirer", payment.AcmeResponseSource);
        Assert.Equal("Approved", payment.AcmeResponseDescription);
        Assert.Equal("A12345", payment.AcmeAuthCode);
        Assert.Equal("recon-1", payment.AcmeReconId);
        Assert.Equal(rawPayload, payment.AcmeResponsePayload);
    }

    [Fact]
    public async Task PaymentAdvice_WithoutProviderTimestampCannotRollBackKnownApproval()
    {
        var payment = ExistingPayment();
        payment.Status = PaymentStatus.Approved;
        payment.LastPaymentOutcomeAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        payment.AuthorizedAmountMinorUnits = 5000;
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.PaymentAdvice, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var advice = Advice("05") with { TransactionStatus = "Failure", TransactionDateTime = null };

        await fixture.Service.ProcessPaymentAdviceAsync(advice, "{}", CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(5000, payment.AuthorizedAmountMinorUnits);
        Assert.DoesNotContain(fixture.LifecycleEvents, x => x.Type == PaymentEventType.SubmissionFailed);
    }

    [Fact]
    public async Task DuplicatePaymentAdvice_IsRecordedButNotAppliedTwice()
    {
        var payment = ExistingPayment();
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.PaymentAdvice, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await fixture.Service.ProcessPaymentAdviceAsync(Advice("COMPL"), "{}", CancellationToken.None);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Empty(fixture.LifecycleEvents);
        Assert.Contains(fixture.Notifications, x => x.ProcessingStatus == NotificationProcessingStatus.IgnoredAsDuplicate);
    }

    [Fact]
    public async Task RefundWebhook_AppliesPartialRefundAsSeparateLifecycleEvent()
    {
        var payment = ExistingPayment();
        payment.Status = PaymentStatus.Approved;
        payment.AuthorizedAmountMinorUnits = 5000;
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.TransactionStatus, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await fixture.Service.ProcessTransactionStatusAsync(Refund(Guid.NewGuid(), 2000), "{}", CancellationToken.None);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Contains(fixture.LifecycleEvents, x => x.Type == PaymentEventType.RefundApplied && x.AmountMinorUnits == 2000);
    }

    [Fact]
    public async Task RefundWebhook_RejectsAmountAboveRemainingAuthorizedBalance()
    {
        var payment = ExistingPayment();
        payment.Status = PaymentStatus.PartiallyRefunded;
        payment.AuthorizedAmountMinorUnits = 5000;
        var fixture = new NotificationFixture(payment);
        var priorRefund = new PaymentLifecycleEvent { Id = Guid.NewGuid(), PaymentId = payment.Id, Type = PaymentEventType.RefundApplied, AmountMinorUnits = 3500 };
        fixture.LifecycleEvents.Add(priorRefund);
        payment.LifecycleEvents.Add(priorRefund);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.TransactionStatus, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await fixture.Service.ProcessTransactionStatusAsync(Refund(Guid.NewGuid(), 2000), "{}", CancellationToken.None);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.DoesNotContain(fixture.LifecycleEvents, x => x.Type == PaymentEventType.RefundApplied && x.AmountMinorUnits == 2000);
        Assert.Contains(fixture.Notifications, x => x.ProcessingStatus == NotificationProcessingStatus.Rejected);
    }

    [Fact]
    public async Task TransactionStatusDuplicate_IsNotAppliedAgain()
    {
        var payment = ExistingPayment();
        payment.Status = PaymentStatus.Approved;
        payment.AuthorizedAmountMinorUnits = 5000;
        var eventId = Guid.NewGuid();
        var fixture = new NotificationFixture(payment);
        fixture.NotificationExists.Setup(x => x.ExistsAsync(NotificationSource.TransactionStatus, eventId.ToString("D"), eventId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await fixture.Service.ProcessTransactionStatusAsync(Refund(eventId, 2000), "{}", CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.DoesNotContain(fixture.LifecycleEvents, x => x.Type == PaymentEventType.RefundApplied);
    }

    private static NotificationApplicationService CreateService() => new(
        Mock.Of<IPaymentRepository>(), Mock.Of<IInboundNotificationRepository>(), Mock.Of<IUnitOfWork>(), Options,
        NullLogger<NotificationApplicationService>.Instance);

    private static NotificationOptions Options => new()
    {
        AdviceUsername = AdviceUser,
        AdvicePassword = AdvicePassword,
        WebhookSecret = WebhookSecret,
        MerchantId = MerchantId
    };

    private static Payment ExistingPayment() => new()
    {
        Id = Guid.NewGuid(),
        AppPaymentId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        SubMerchantId = Guid.NewGuid(),
        AmountMinorUnits = 5000,
        CurrencyCode = "ZAR",
        Status = PaymentStatus.Pending,
        AcmeTransactionId = "acme-tx-1",
        AcmeOrderNumber = "order-1",
        RetrievalReferenceNumber = "123456789012",
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
    };

    private static AcmePaymentAdviceNotification Advice(string code) => new()
    {
        RequestType = "Payment",
        MerchantID = MerchantId,
        TransactionID = "acme-tx-1",
        TransactionDateTime = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
        OrderNumber = "order-1",
        RetrievalReferenceNumber = "123456789012",
        SystemsTraceAuditNumber = "123456",
        Amount = 5000,
        CurrencyCode = "ZAR",
        PaymentService = "CardNotPresent",
        TransactionStatus = code == "PEND" ? "Other" : "Success",
        ResponseSource = "Acme",
        ResponseCode = code,
        CommProtocol = "REST"
    };

    private static AcmeTransactionStatusNotification Refund(Guid eventId, long amount) => new()
    {
        TransactionUUID = eventId.ToString("D"),
        TransactionType = "Refund",
        RetrievalReferenceNumber = "123456789012",
        TransactionAmount = amount,
        CurrencyCode = "ZAR",
        TransactionResultCode = "00",
        IsApproved = true
    };

    private sealed class NotificationFixture
    {
        public Mock<IPaymentRepository> Payments { get; } = new(MockBehavior.Loose);
        public Mock<IInboundNotificationRepository> NotificationExists { get; } = new(MockBehavior.Loose);
        public Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Loose);
        public List<InboundNotification> Notifications { get; } = [];
        public List<PaymentLifecycleEvent> LifecycleEvents { get; } = [];
        public NotificationApplicationService Service { get; }

        public NotificationFixture(Payment payment)
        {
            Payments.Setup(x => x.LockByAcmeTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Payments.Setup(x => x.LockByRetrievalReferenceNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Payments.Setup(x => x.GetByAcmeTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(payment);
            Payments.Setup(x => x.GetByRetrievalReferenceNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(payment);
            Payments.Setup(x => x.AddLifecycleEventAsync(It.IsAny<PaymentLifecycleEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PaymentLifecycleEvent, CancellationToken>((value, _) => LifecycleEvents.Add(value)).Returns(Task.CompletedTask);
            NotificationExists.Setup(x => x.AcquireDeduplicationLockAsync(It.IsAny<NotificationSource>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            NotificationExists.Setup(x => x.AddAsync(It.IsAny<InboundNotification>(), It.IsAny<CancellationToken>()))
                .Callback<InboundNotification, CancellationToken>((value, _) => Notifications.Add(value)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FakeTransaction());
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Service = new NotificationApplicationService(Payments.Object, NotificationExists.Object, UnitOfWork.Object, Options,
                NullLogger<NotificationApplicationService>.Instance);
        }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
