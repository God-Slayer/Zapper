using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Infrastructure.Persistence;

public sealed class DevelopmentDataSeeder(ZapperDbContext dbContext)
{
    public static readonly Guid CustomerOneId = Guid.Parse("ea238ea1-0fd8-4c55-bfeb-27fb2ea96f30");
    public static readonly Guid CustomerTwoId = Guid.Parse("af0cdac8-25ea-49f2-a9b5-686c53ea350d");
    public static readonly Guid MerchantOneId = Guid.Parse("7c9d3d27-4268-4e69-8305-cc086d48e599");
    public static readonly Guid MerchantTwoId = Guid.Parse("572c3c74-5ad4-4804-8e99-5ab0db146e5e");
    public static readonly Guid CardOneId = Guid.Parse("33dd5936-14fb-44af-b4e4-013d0e9d1757");
    public static readonly Guid CardTwoId = Guid.Parse("8062aa5f-bcdb-4404-bfeb-12de8e3cfaf6");
    public static readonly Guid CardThreeId = Guid.Parse("872355a0-87a7-4ee0-a3b6-34558d5be08a");
    public static readonly Guid ApprovedDemoAppPaymentId = Guid.Parse("6c713d84-0f18-4f07-b929-e05ad4681831");
    public static readonly Guid DeclinedDemoAppPaymentId = Guid.Parse("5c7100fb-06f8-42c7-9ceb-a8c0e4c3860b");
    private static readonly Guid ApprovedDemoPaymentId = Guid.Parse("9a98da64-c0d7-46b9-9b27-54082f029ab7");
    private static readonly Guid DeclinedDemoPaymentId = Guid.Parse("d32b8bca-114a-48a0-b516-8e64cf0b6ee1");
    private static readonly Guid ApprovedResponseMessageId = Guid.Parse("c5f0a940-bc4a-4e59-a1d4-67d000000001");
    private static readonly Guid DeclinedResponseMessageId = Guid.Parse("c5f0a940-bc4a-4e59-a1d4-67d000000002");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customers = new[]
        {
            new Customer { Id = CustomerOneId, EmailAddress = "alex.mokoena@example.test" },
            new Customer { Id = CustomerTwoId, EmailAddress = "sam.naidoo@example.test" }
        };
        var existingCustomerIds = await dbContext.Customers.Select(x => x.Id).ToListAsync(cancellationToken);
        dbContext.Customers.AddRange(customers.Where(x => !existingCustomerIds.Contains(x.Id)));

        var merchants = new[]
        {
            new SubMerchant
            {
                Id = MerchantOneId,
                ZapperMerchantId = "ZAP000000000001",
                TradingName = "Cape Demo Coffee",
                City = "Cape Town",
                RegionCode = "WC",
                CountryCode = "ZA",
                MerchantCategoryCode = "5812"
            },
            new SubMerchant
            {
                Id = MerchantTwoId,
                ZapperMerchantId = "ZAP000000000002",
                TradingName = "Joburg Demo Books",
                City = "Johannesburg",
                RegionCode = "Gauteng",
                CountryCode = "ZA",
                MerchantCategoryCode = "5942"
            }
        };
        var existingMerchantIds = await dbContext.SubMerchants.Select(x => x.Id).ToListAsync(cancellationToken);
        dbContext.SubMerchants.AddRange(merchants.Where(x => !existingMerchantIds.Contains(x.Id)));

        var cards = new[]
        {
            new StoredCard { Id = CardOneId, CustomerId = CustomerOneId, AcmeToken = "tok_demo_customer1_visa" },
            new StoredCard { Id = CardTwoId, CustomerId = CustomerOneId, AcmeToken = "tok_demo_customer1_mc" },
            new StoredCard { Id = CardThreeId, CustomerId = CustomerTwoId, AcmeToken = "tok_demo_customer2_visa" }
        };
        var existingCardIds = await dbContext.StoredCards.Select(x => x.Id).ToListAsync(cancellationToken);
        dbContext.StoredCards.AddRange(cards.Where(x => !existingCardIds.Contains(x.Id)));

        var seededAt = DateTimeOffset.UtcNow;
        var payments = new[]
        {
            new Payment
            {
                Id = ApprovedDemoPaymentId,
                AppPaymentId = ApprovedDemoAppPaymentId,
                CustomerId = CustomerOneId,
                SubMerchantId = MerchantOneId,
                AmountMinorUnits = 1250,
                CurrencyCode = "ZAR",
                Status = PaymentStatus.Approved,
                AcmeTransactionId = "02111ee8-d057-467b-9c3f-7030426c6151",
                AcmeOrderNumber = "1000000000010001",
                RetrievalReferenceNumber = "100000000001",
                AuthorizedAmountMinorUnits = 1250,
                AcmeResponseCode = "00",
                AcmeResponseMessageId = ApprovedResponseMessageId.ToString(),
                AcmeTransactionStatus = "Success",
                AcmeResponseSource = "Acquirer",
                AcmeResponseDescription = "Approved",
                AcmeClientMessage = "Approved",
                AcmeReconId = "100000000001",
                AcmeAuthCode = "A12345",
                AcmeResponsePayload = BuildResponsePayload(
                    ApprovedResponseMessageId,
                    "02111ee8-d057-467b-9c3f-7030426c6151",
                    "1000000000010001",
                    "100000000001",
                    "100000000001",
                    "A12345",
                    "Success",
                    "00",
                    "Approved",
                    "Approved",
                    1250,
                    seededAt),
                CreatedAt = seededAt,
                UpdatedAt = seededAt,
                LastPaymentOutcomeAt = seededAt,
                LifecycleEvents = new List<PaymentLifecycleEvent>
                {
                    new()
                    {
                        Id = Guid.Parse("6a0f4354-3bd7-4755-8a2c-544581321bf7"),
                        PaymentId = ApprovedDemoPaymentId,
                        Type = PaymentEventType.Submitted,
                        AmountMinorUnits = 1250,
                        CurrencyCode = "ZAR",
                        OccurredAt = seededAt,
                        RecordedAt = seededAt,
                        Source = "DevelopmentSeed"
                    },
                    new()
                    {
                        Id = Guid.Parse("84fd4ced-f401-4691-8b62-4ec76ee84ef4"),
                        PaymentId = ApprovedDemoPaymentId,
                        Type = PaymentEventType.Approved,
                        AmountMinorUnits = 1250,
                        CurrencyCode = "ZAR",
                        OccurredAt = seededAt,
                        RecordedAt = seededAt,
                        Source = "DevelopmentSeed",
                        ResponseCode = "00",
                        ExternalEventId = $"DevelopmentSeed:02111ee8-d057-467b-9c3f-7030426c6151:Success:00",
                        Description = "Approved"
                    }
                }
            },
            new Payment
            {
                Id = DeclinedDemoPaymentId,
                AppPaymentId = DeclinedDemoAppPaymentId,
                CustomerId = CustomerTwoId,
                SubMerchantId = MerchantTwoId,
                AmountMinorUnits = 2500,
                CurrencyCode = "ZAR",
                Status = PaymentStatus.Declined,
                AcmeTransactionId = "b1a2e515-f0f2-45c2-aadc-2cdfd24e9655",
                AcmeOrderNumber = "2000000000010001",
                RetrievalReferenceNumber = "200000000001",
                AcmeResponseCode = "05",
                AcmeResponseMessageId = DeclinedResponseMessageId.ToString(),
                AcmeTransactionStatus = "Success",
                AcmeResponseSource = "Acquirer",
                AcmeResponseDescription = "Do not honour",
                AcmeClientMessage = "Do not honour",
                AcmeReconId = "200000000001",
                AcmeResponsePayload = BuildResponsePayload(
                    DeclinedResponseMessageId,
                    "b1a2e515-f0f2-45c2-aadc-2cdfd24e9655",
                    "2000000000010001",
                    "200000000001",
                    "200000000001",
                    null,
                    "Success",
                    "05",
                    "Do not honour",
                    "Do not honour",
                    null,
                    seededAt),
                CreatedAt = seededAt,
                UpdatedAt = seededAt,
                LastPaymentOutcomeAt = seededAt,
                LifecycleEvents = new List<PaymentLifecycleEvent>
                {
                    new()
                    {
                        Id = Guid.Parse("705b88bc-d0bd-4736-ac68-e37d9f6361af"),
                        PaymentId = DeclinedDemoPaymentId,
                        Type = PaymentEventType.Submitted,
                        AmountMinorUnits = 2500,
                        CurrencyCode = "ZAR",
                        OccurredAt = seededAt,
                        RecordedAt = seededAt,
                        Source = "DevelopmentSeed"
                    },
                    new()
                    {
                        Id = Guid.Parse("c5f93b85-91a5-4372-932a-84a1207a770a"),
                        PaymentId = DeclinedDemoPaymentId,
                        Type = PaymentEventType.Declined,
                        AmountMinorUnits = 2500,
                        CurrencyCode = "ZAR",
                        OccurredAt = seededAt,
                        RecordedAt = seededAt,
                        Source = "DevelopmentSeed",
                        ResponseCode = "05",
                        ExternalEventId = $"DevelopmentSeed:b1a2e515-f0f2-45c2-aadc-2cdfd24e9655:Success:05",
                        Description = "Do not honour"
                    }
                }
            }
        };
        var seededAppPaymentIds = payments.Select(x => x.AppPaymentId).ToArray();
        var existingPayments = await dbContext.Payments
            .Where(x => seededAppPaymentIds.Contains(x.AppPaymentId))
            .ToListAsync(cancellationToken);
        var existingPaymentIds = existingPayments.Select(x => x.Id).ToArray();
        var existingLifecycleEvents = await dbContext.PaymentLifecycleEvents
            .Where(x => existingPaymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var existingAppPaymentIds = existingPayments.Select(x => x.AppPaymentId).ToHashSet();
        dbContext.Payments.AddRange(payments.Where(x => !existingAppPaymentIds.Contains(x.AppPaymentId)));

        foreach (var existingPayment in existingPayments)
        {
            var seed = payments.Single(x => x.AppPaymentId == existingPayment.AppPaymentId);
            var expectedResponsePayload = BuildResponsePayload(
                existingPayment.AppPaymentId == ApprovedDemoAppPaymentId ? ApprovedResponseMessageId : DeclinedResponseMessageId,
                seed.AcmeTransactionId,
                seed.AcmeOrderNumber,
                seed.RetrievalReferenceNumber!,
                seed.AcmeReconId!,
                seed.AcmeAuthCode,
                seed.AcmeTransactionStatus!,
                seed.AcmeResponseCode!,
                seed.AcmeResponseDescription!,
                seed.AcmeClientMessage!,
                seed.AuthorizedAmountMinorUnits,
                existingPayment.LastPaymentOutcomeAt ?? existingPayment.CreatedAt);
            var responseFieldsChanged = existingPayment.Status != seed.Status
                || existingPayment.RetrievalReferenceNumber != seed.RetrievalReferenceNumber
                || existingPayment.AuthorizedAmountMinorUnits != seed.AuthorizedAmountMinorUnits
                || existingPayment.AcmeResponseCode != seed.AcmeResponseCode
                || existingPayment.AcmeResponseMessageId != seed.AcmeResponseMessageId
                || existingPayment.AcmeTransactionStatus != seed.AcmeTransactionStatus
                || existingPayment.AcmeResponseSource != seed.AcmeResponseSource
                || existingPayment.AcmeResponseDescription != seed.AcmeResponseDescription
                || existingPayment.AcmeClientMessage != seed.AcmeClientMessage
                || existingPayment.AcmeReconId != seed.AcmeReconId
                || existingPayment.AcmeAuthCode != seed.AcmeAuthCode
                || existingPayment.AcmeOtherTransactionId != seed.AcmeOtherTransactionId
                || existingPayment.AcmeResponsePayload != expectedResponsePayload;

            existingPayment.Status = seed.Status;
            existingPayment.RetrievalReferenceNumber = seed.RetrievalReferenceNumber;
            existingPayment.AuthorizedAmountMinorUnits = seed.AuthorizedAmountMinorUnits;
            existingPayment.AcmeResponseCode = seed.AcmeResponseCode;
            existingPayment.AcmeResponseMessageId = seed.AcmeResponseMessageId;
            existingPayment.AcmeTransactionStatus = seed.AcmeTransactionStatus;
            existingPayment.AcmeResponseSource = seed.AcmeResponseSource;
            existingPayment.AcmeResponseDescription = seed.AcmeResponseDescription;
            existingPayment.AcmeClientMessage = seed.AcmeClientMessage;
            existingPayment.AcmeReconId = seed.AcmeReconId;
            existingPayment.AcmeAuthCode = seed.AcmeAuthCode;
            existingPayment.AcmeOtherTransactionId = seed.AcmeOtherTransactionId;
            existingPayment.AcmeResponsePayload = expectedResponsePayload;
            if (responseFieldsChanged)
                existingPayment.UpdatedAt = DateTimeOffset.UtcNow;

            foreach (var seededEvent in seed.LifecycleEvents)
            {
                if (existingLifecycleEvents.TryGetValue(seededEvent.Id, out var existingEvent))
                {
                    var eventEntry = dbContext.Entry(existingEvent);
                    eventEntry.Property(x => x.PaymentId).CurrentValue = existingPayment.Id;
                    eventEntry.Property(x => x.Type).CurrentValue = seededEvent.Type;
                    eventEntry.Property(x => x.AmountMinorUnits).CurrentValue = seededEvent.AmountMinorUnits;
                    eventEntry.Property(x => x.CurrencyCode).CurrentValue = seededEvent.CurrencyCode;
                    eventEntry.Property(x => x.OccurredAt).CurrentValue = existingPayment.CreatedAt;
                    eventEntry.Property(x => x.RecordedAt).CurrentValue = existingPayment.CreatedAt;
                    eventEntry.Property(x => x.Source).CurrentValue = seededEvent.Source;
                    eventEntry.Property(x => x.ExternalEventId).CurrentValue = seededEvent.ExternalEventId;
                    eventEntry.Property(x => x.ResponseCode).CurrentValue = seededEvent.ResponseCode;
                    eventEntry.Property(x => x.Description).CurrentValue = seededEvent.Description;
                }
                else
                    dbContext.PaymentLifecycleEvents.Add(seededEvent);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string BuildResponsePayload(
        Guid messageId,
        string transactionId,
        string orderNumber,
        string rrn,
        string reconId,
        string? authCode,
        string transactionStatus,
        string responseCode,
        string description,
        string clientMessage,
        long? authAmount,
        DateTimeOffset responseAt)
    {
        var responseTimestamp = responseAt.ToOffset(TimeSpan.FromHours(2))
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var response = new
        {
            MessageHeader = new
            {
                MessageDateTime = responseTimestamp,
                MessageID = messageId.ToString(),
                Channel = "App"
            },
            PaymentResponse = new
            {
                TransactionID = transactionId,
                TransactionDateTime = responseTimestamp,
                OrderNumber = orderNumber,
                ReconID = reconId,
                RetrievalReferenceNumber = rrn,
                AuthCode = authCode,
                TransactionStatus = transactionStatus,
                ResponseDetail = new
                {
                    Source = "Acquirer",
                    Code = responseCode,
                    Description = description,
                    ClientMessage = clientMessage
                },
                AuthAmount = authAmount
            }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}