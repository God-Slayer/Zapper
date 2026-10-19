using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Zapper.Application.Reporting;

namespace Zapper.Api.Controllers.Reports;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IDailySettlementSummaryService summaryService) : ControllerBase
{
    [HttpGet("daily-summary")]
    [ProducesResponseType<DailySettlementSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDailySummary([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        if (date is null || date.Value == DateOnly.MaxValue)
            return BadRequest(new { code = "DATE_REQUIRED", message = "Query parameter 'date' is required in YYYY-MM-DD format." });

        var summary = await summaryService.GetAsync(date.Value, cancellationToken);
        return Ok(summary);
    }
}