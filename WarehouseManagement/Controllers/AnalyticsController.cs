using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace WarehouseManagement.Controllers;
[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,Manager")]
public sealed class AnalyticsController(IWarehouseEventService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(DateTime? start, DateTime? end, string? type, CancellationToken ct) => Ok(await service.GetAnalyticsSummaryAsync(start, end, type, ct));
}
