using Moq;
using Zapper.Application.Abstractions;
using Zapper.Application.Reporting;
using Zapper.Domain.Enums;

namespace Zapper.Tests;

public sealed class DailySettlementSummaryServiceTests
{
    [Fact]
    public async Task DailySummary_GroupsByMerchantAndCurrencyAndPreservesLifecycleTotals()
    {
        var merchantId = Guid.NewGuid();
        var rows = new DailySettlementEventTotal[]
        {
            new(merchantId, "MERCHANT-A", "Alpha Shop", "ZAR", PaymentEventType.Approved, 2, 9000),
            new(merchantId, "MERCHANT-A", "Alpha Shop", "ZAR", PaymentEventType.Declined, 1, 1200),
            new(merchantId, "MERCHANT-A", "Alpha Shop", "ZAR", PaymentEventType.Reversed, 1, 3000),
            new(merchantId, "MERCHANT-A", "Alpha Shop", "ZAR", PaymentEventType.RefundApplied, 2, 2500),
            new(Guid.NewGuid(), "MERCHANT-B", "Beta Store", "USD", PaymentEventType.Approved, 1, 700)
        };
        var repository = new Mock<IDailySettlementSummaryRepository>();
        repository.Setup(x => x.GetEventTotalsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var service = new DailySettlementSummaryService(repository.Object);

        var summary = await service.GetAsync(new DateOnly(2026, 10, 17), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 17), summary.Date);
        var alpha = Assert.Single(summary.SubMerchants.Where(x => x.ZapperMerchantId == "MERCHANT-A"));
        Assert.Equal((2, 9000), (alpha.Approved.Count, alpha.Approved.TotalAmountMinorUnits));
        Assert.Equal((1, 1200), (alpha.Declined.Count, alpha.Declined.TotalAmountMinorUnits));
        Assert.Equal((1, 3000), (alpha.Reversals.Count, alpha.Reversals.TotalAmountMinorUnits));
        Assert.Equal((2, 2500), (alpha.Refunds.Count, alpha.Refunds.TotalAmountMinorUnits));
        Assert.Equal("Africa/Johannesburg", summary.TimeZone);
        repository.Verify(x => x.GetEventTotalsAsync(
            new DateTimeOffset(2026, 10, 16, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 17, 22, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }
}