using Zapper.Application.Reporting;

namespace Zapper.Application.Abstractions;

public interface IDailySettlementSummaryRepository
{
    Task<IReadOnlyList<DailySettlementEventTotal>> GetEventTotalsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken);
}