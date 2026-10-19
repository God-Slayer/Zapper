using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Zapper.Application.Abstractions;
using Zapper.Application.AcmePay;
using Zapper.Application.Payments;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Tests;

public sealed class PaymentServiceTests
{
    [Theory]
    [InlineData("00", "Success", PaymentStatus.Approved)]
    [InlineData("COMPL", "Complete", PaymentStatus.Approved)]
    [InlineData("PEND", "Other", PaymentStatus.Processing)]
    [InlineData("05", "Success", PaymentStatus.Declined)]
    public async Task Submit_MapsProviderResponseToExpectedPaymentState(string code, string providerStatus, PaymentStatus expected)
    {
        var fixture = new PaymentFixture(Response(code, providerStatus));

        var result = await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);

        Assert.Equal(expected, result.Payment.Status);
        Assert.Equal(code, result.Payment.AcmeResponseCode);
        Assert.Single(fixture.Events);
        Assert.Equal(expected == PaymentStatus.Approved ? PaymentEventType.Approved : expected == PaymentStatus.Declined ? PaymentEventType.Declined : PaymentEventType.Processing, fixture.Events[0].Type);
    }

    [Fact]
    public async Task Submit_FormatsCardAcceptorDescriptorIntoRequiredFortyPositions()
    {
        var fixture = new PaymentFixture(Response("00", "Success"), region: "Gauteng");

        await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);

        var descriptor = Assert.IsType<string>(fixture.AcmeRequest!.PaymentRequest.CardAcceptorName);
        Assert.Equal(40, descriptor.Length);
        Assert.Equal("Ga", descriptor.Substring(36, 2));
        Assert.Equal("ZA", descriptor.Substring(38, 2));
        Assert.Equal("ZAP*Demo Books", descriptor[..14].TrimEnd());
    }

    [Fact]
    public async Task SamePaymentIdAndPayload_ReturnsExistingPaymentWithoutCallingAcmeAgain()
    {
        var fixture = new PaymentFixture(Response("00", "Success"));
        var existing = PaymentFor(fixture.Command, fixture.Customer.Id, fixture.Merchant.Id);
        existing.Status = PaymentStatus.Approved;
        existing.AuthorizedAmountMinorUnits = 5000;
        fixture.Payments.Setup(x => x.GetByAppPaymentIdAsync(fixture.Command.AppPaymentId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);

        Assert.True(result.IsReplay);
        Assert.Equal(existing.Id, result.Payment.PaymentId);
        fixture.Acme.Verify(x => x.SubmitPaymentAsync(It.IsAny<AcmePaymentRequestEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReusingPaymentIdWithDifferentAmount_ReturnsConflict()
    {
        var fixture = new PaymentFixture(Response("00", "Success"));
        var existing = PaymentFor(fixture.Command, fixture.Customer.Id, fixture.Merchant.Id);
        fixture.Payments.Setup(x => x.GetByAppPaymentIdAsync(fixture.Command.AppPaymentId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var changed = fixture.Command with { AmountMinorUnits = fixture.Command.AmountMinorUnits + 1 };

        var exception = await Assert.ThrowsAsync<PaymentApplicationException>(() => fixture.Service.SubmitAsync(changed, CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_CONFLICT", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        fixture.Acme.Verify(x => x.SubmitPaymentAsync(It.IsAny<AcmePaymentRequestEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Timeout_ReconcilesUsingTheAlreadyReservedAcmeIdentifiers()
    {
        var fixture = new PaymentFixture(Response("00", "Success"));
        fixture.Acme.Setup(x => x.SubmitPaymentAsync(It.IsAny<AcmePaymentRequestEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("simulated timeout"));
        fixture.Acme.Setup(x => x.QueryTransactionAsync("MERCHANT1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("00", "Success"));

        var result = await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, result.Payment.Status);
        Assert.NotNull(fixture.ReservedPayment);
        fixture.Acme.Verify(x => x.QueryTransactionAsync("MERCHANT1", fixture.ReservedPayment!.AcmeTransactionId,
            fixture.ReservedPayment.AcmeOrderNumber, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconciliation_DoesNotAppendAnIdenticalPendingLifecycleOutcome()
    {
        var fixture = new PaymentFixture(Response("PEND", "Processing"));
        var payment = PaymentFor(fixture.Command, fixture.Customer.Id, fixture.Merchant.Id);
        payment.Status = PaymentStatus.Processing;
        payment.AcmeResponseCode = "PEND";
        payment.AcmeTransactionStatus = "Processing";
        payment.LifecycleEvents.Add(new PaymentLifecycleEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Type = PaymentEventType.Processing,
            AmountMinorUnits = payment.AmountMinorUnits,
            CurrencyCode = payment.CurrencyCode,
            OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            RecordedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            Source = "AcmeResponse",
            ResponseCode = "PEND"
        });
        fixture.ExistingPayment = payment;
        fixture.Acme.Setup(x => x.QueryTransactionAsync("MERCHANT1", payment.AcmeTransactionId, payment.AcmeOrderNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("PEND", "Processing"));

        var result = await fixture.Service.GetByAppPaymentIdAsync(payment.AppPaymentId, CancellationToken.None);

        Assert.Equal(PaymentStatus.Processing, result!.Status);
        Assert.Empty(fixture.Events);
        fixture.Payments.Verify(x => x.AddLifecycleEventAsync(It.IsAny<PaymentLifecycleEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task E29Retry_ReusesTheOriginalPaymentAndAcmeTransactionIdentifiers()
    {
        var fixture = new PaymentFixture(Response("00", "Success"));
        var calls = 0;
        fixture.Acme.Setup(x => x.SubmitPaymentAsync(It.IsAny<AcmePaymentRequestEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<AcmePaymentRequestEnvelope, CancellationToken>((request, _) => fixture.AcmeRequests.Add(request))
            .Returns((AcmePaymentRequestEnvelope _, CancellationToken _) => ++calls == 1
                ? Task.FromException<AcmePaymentResponseEnvelope>(new AcmePayException("E29", "Throttled", 429, retryAfter: TimeSpan.FromSeconds(1)))
                : Task.FromResult(Response("00", "Success")));

        var throttled = await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);
        Assert.Equal(PaymentStatus.SubmissionFailed, throttled.Payment.Status);
        Assert.Equal("E29", throttled.Payment.AcmeResponseCode);
        Assert.Equal(TimeSpan.FromSeconds(1), throttled.UpstreamRetryAfter);

        var retried = await fixture.Service.SubmitAsync(fixture.Command, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, retried.Payment.Status);
        Assert.Equal(throttled.Payment.PaymentId, retried.Payment.PaymentId);
        Assert.Equal(fixture.AcmeRequests[0].PaymentRequest.TransactionID, fixture.AcmeRequests[1].PaymentRequest.TransactionID);
        Assert.Equal(fixture.AcmeRequests[0].PaymentRequest.OrderNumber, fixture.AcmeRequests[1].PaymentRequest.OrderNumber);
        Assert.Equal(2, fixture.Acme.Invocations.Count(x => x.Method.Name == nameof(IAcmePayClient.SubmitPaymentAsync)));
    }

    private static AcmePaymentResponseEnvelope Response(string code, string status) => new()
    {
        MessageHeader = new AcmeMessageHeader { MessageID = "message-1" },
        PaymentResponse = new AcmePaymentResponse
        {
            TransactionID = "transaction-1",
            TransactionStatus = status,
            AuthAmount = 5000,
            RetrievalReferenceNumber = "123456789012",
            ResponseDetail = new AcmeResponseDetail { Code = code }
        }
    };

    private static Payment PaymentFor(SubmitPaymentCommand command, Guid customerId, Guid merchantId) => new()
    {
        Id = Guid.NewGuid(),
        AppPaymentId = command.AppPaymentId,
        CustomerId = customerId,
        SubMerchantId = merchantId,
        AmountMinorUnits = command.AmountMinorUnits,
        CurrencyCode = command.CurrencyCode ?? "ZAR",
        Status = PaymentStatus.Pending,
        AcmeTransactionId = "transaction-1",
        AcmeOrderNumber = "order-1",
        RetrievalReferenceNumber = "123456789012",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class PaymentFixture
    {
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid MerchantId { get; } = Guid.NewGuid();
        public Customer Customer { get; }
        public SubMerchant Merchant { get; }
        public SubmitPaymentCommand Command { get; }
        public Mock<IPaymentRepository> Payments { get; } = new(MockBehavior.Loose);
        public Mock<ICustomerRepository> Customers { get; } = new(MockBehavior.Loose);
        public Mock<ISubMerchantRepository> Merchants { get; } = new(MockBehavior.Loose);
        public Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Loose);
        public Mock<IAcmePayClient> Acme { get; } = new(MockBehavior.Strict);
        public List<PaymentLifecycleEvent> Events { get; } = [];
        public List<AcmePaymentRequestEnvelope> AcmeRequests { get; } = [];
        public AcmePaymentRequestEnvelope? AcmeRequest { get; private set; }
        public Payment? ReservedPayment { get; private set; }
        public Payment? ExistingPayment { get; set; }
        public PaymentService Service { get; }

        public PaymentFixture(AcmePaymentResponseEnvelope response, string region = "Western Cape")
        {
            Customer = new Customer { Id = CustomerId, EmailAddress = "demo@example.test", StoredCards = [new StoredCard { Id = Guid.NewGuid(), CustomerId = CustomerId, AcmeToken = "token" }] };
            Merchant = new SubMerchant { Id = MerchantId, ZapperMerchantId = "DEMO-MERCHANT", TradingName = "Demo Books", City = "Cape Town", RegionCode = region, CountryCode = "ZA", MerchantCategoryCode = "5942" };
            Command = new SubmitPaymentCommand(CustomerId, Merchant.ZapperMerchantId, 5000, "ZAR", Guid.NewGuid());

            Customers.Setup(x => x.GetWithStoredCardsAsync(CustomerId, It.IsAny<CancellationToken>())).ReturnsAsync(Customer);
            Merchants.Setup(x => x.GetByZapperMerchantIdAsync(Merchant.ZapperMerchantId, It.IsAny<CancellationToken>())).ReturnsAsync(Merchant);
            Payments.Setup(x => x.GetByAppPaymentIdAsync(Command.AppPaymentId, It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<Payment?>(ReservedPayment ?? ExistingPayment));
            Payments.Setup(x => x.GetByRetrievalReferenceNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Payment?)null);
            Payments.Setup(x => x.TryAddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
                .Callback<Payment, CancellationToken>((value, _) => ReservedPayment ??= value).ReturnsAsync(true);
            Payments.Setup(x => x.AddLifecycleEventAsync(It.IsAny<PaymentLifecycleEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PaymentLifecycleEvent, CancellationToken>((value, _) => Events.Add(value)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Acme.Setup(x => x.SubmitPaymentAsync(It.IsAny<AcmePaymentRequestEnvelope>(), It.IsAny<CancellationToken>()))
                .Callback<AcmePaymentRequestEnvelope, CancellationToken>((value, _) => AcmeRequest = value).ReturnsAsync(response);
            Service = new PaymentService(Customers.Object, Merchants.Object, Payments.Object, UnitOfWork.Object, Acme.Object,
                new PaymentServiceOptions { AcmeMerchantId = "MERCHANT1", FacilitatorName = "ZAP" }, NullLogger<PaymentService>.Instance);
        }
    }
}
