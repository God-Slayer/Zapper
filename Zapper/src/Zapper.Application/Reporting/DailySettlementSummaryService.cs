using Zapper.Application.Abstractions;
using Zapper.Domain.Enums;

namespace Zapper.Application.Reporting;

public sealed class DailySettlementSummaryService(IDailySettlementSummaryRepository repository) : IDailySettlementSummaryService
{
    public async Task<DailySettlementSummary> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = Zapper.Application.Time.SouthAfricanTime.GetBusinessDateUtcRange(date);

        var eventTotals = await repository.GetEventTotalsAsync(startUtc, endUtc, cancellationToken);
        var summaries = eventTotals
            .GroupBy(x => new { x.SubMerchantId, x.ZapperMerchantId, x.TradingName, x.CurrencyCode })
            .Select(group => new SubMerchantSettlementSummary(
                group.Key.SubMerchantId,
                group.Key.ZapperMerchantId,
                group.Key.TradingName,
                group.Key.CurrencyCode,
                ToMetric(group, PaymentEventType.Approved),
                ToMetric(group, PaymentEventType.Declined),
                ToMetric(group, PaymentEventType.Reversed),
                ToMetric(group, PaymentEventType.RefundApplied)))
            .OrderBy(x => x.TradingName, StringComparer.Ordinal)
            .ThenBy(x => x.CurrencyCode, StringComparer.Ordinal)
            .ToArray();

        return new DailySettlementSummary(date, Zapper.Application.Time.SouthAfricanTime.TimeZoneId, summaries);
    }

    private static SettlementMetric ToMetric(IEnumerable<DailySettlementEventTotal> events, PaymentEventType type)
    {
        var matches = events.Where(x => x.EventType == type);
        return new SettlementMetric(matches.Sum(x => x.Count), matches.Sum(x => x.AmountMinorUnits));
    }
}