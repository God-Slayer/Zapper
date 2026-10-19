using Microsoft.EntityFrameworkCore;
using Zapper.Application.Abstractions;
using Zapper.Application.Reporting;
using Zapper.Domain.Enums;
using Zapper.Infrastructure.Persistence;

namespace Zapper.Infrastructure.Repositories;

public sealed class DailySettlementSummaryRepository(ZapperDbContext dbContext) : IDailySettlementSummaryRepository
{
    public async Task<IReadOnlyList<DailySettlementEventTotal>> GetEventTotalsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken) =>
        await (from lifecycleEvent in dbContext.PaymentLifecycleEvents.AsNoTracking()
               join payment in dbContext.Payments.AsNoTracking() on lifecycleEvent.PaymentId equals payment.Id
               join subMerchant in dbContext.SubMerchants.AsNoTracking() on payment.SubMerchantId equals subMerchant.Id
               where lifecycleEvent.OccurredAt >= startUtc && lifecycleEvent.OccurredAt < endUtc
                     && (lifecycleEvent.Type == PaymentEventType.Approved
                         || lifecycleEvent.Type == PaymentEventType.Declined
                         || lifecycleEvent.Type == PaymentEventType.Reversed
                         || lifecycleEvent.Type == PaymentEventType.RefundApplied)
               group lifecycleEvent by new
               {
                   payment.SubMerchantId,
                   subMerchant.ZapperMerchantId,
                   subMerchant.TradingName,
                   lifecycleEvent.CurrencyCode,
                   lifecycleEvent.Type
               }
            into grouped
               select new DailySettlementEventTotal(
                   grouped.Key.SubMerchantId,
                   grouped.Key.ZapperMerchantId,
                   grouped.Key.TradingName,
                   grouped.Key.CurrencyCode,
                   grouped.Key.Type,
                   grouped.Count(),
                   grouped.Sum(x => x.AmountMinorUnits ?? 0)))
            .ToListAsync(cancellationToken);
}