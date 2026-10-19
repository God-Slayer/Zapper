using Zapper.Domain.Enums;

namespace Zapper.Application.Reporting;

public sealed record DailySettlementEventTotal(
    Guid SubMerchantId,
    string ZapperMerchantId,
    string TradingName,
    string CurrencyCode,
    PaymentEventType EventType,
    int Count,
    long AmountMinorUnits);

public sealed record SettlementMetric(int Count, long TotalAmountMinorUnits);

public sealed record SubMerchantSettlementSummary(
    Guid SubMerchantId,
    string ZapperMerchantId,
    string TradingName,
    string CurrencyCode,
    SettlementMetric Approved,
    SettlementMetric Declined,
    SettlementMetric Reversals,
    SettlementMetric Refunds);

public sealed record DailySettlementSummary(
    DateOnly Date,
    string TimeZone,
    IReadOnlyList<SubMerchantSettlementSummary> SubMerchants);