using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseManagement.Services;
namespace WarehouseManagement.Controllers;
[ApiController, Route("api/generate"), Authorize(Roles = "Admin")]
public sealed class GenerateController(EventGenerator generator) : ControllerBase
{
    [HttpPost("events")]
    public async Task<IActionResult> Events([FromQuery] int count, CancellationToken ct)
        => Ok(new { created = await generator.GenerateAsync(count, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct) });
}
