namespace Zapper.Application.Reporting;

public interface IDailySettlementSummaryService
{
    Task<DailySettlementSummary> GetAsync(DateOnly date, CancellationToken cancellationToken);
}