using System.Security.Claims;
using System.Text;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WarehouseManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Clerk")]
public sealed class WarehouseEventsController(
    IWarehouseEventService service,
    ILogger<WarehouseEventsController> logger) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll(DateTime? start, DateTime? end, string? type, CancellationToken ct)
    {
        var events = await service.GetEventsAsync(start, end, type, ct);
        return Ok(events);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var record = await service.GetEventByIdAsync(id, ct);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpGet("filter")]
    public Task<IActionResult> Filter([FromQuery] DateTime start, [FromQuery] DateTime end, CancellationToken ct)
    {
        return GetAll(start, end, null, ct);
    }

    [HttpGet("type/{type}")]
    public Task<IActionResult> ByType(string type, CancellationToken ct)
    {
        return GetAll(null, null, type, ct);
    }

    [HttpPost]
    public async Task<IActionResult> Create(WarehouseEventInput input, CancellationToken ct)
    {
        var record = await service.AddEventAsync(input, UserId, ct);
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(int id, WarehouseEventInput input, CancellationToken ct)
    {
        var record = await service.UpdateEventAsync(id, input, UserId, ct);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await service.DeleteEventAsync(id, ct))
        {
            return NotFound();
        }

        logger.LogWarning("User {UserId} deleted event {EventId}", UserId, id);
        return NoContent();
    }

    [HttpGet("report/csv")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Csv(DateTime? start, DateTime? end, string? type, CancellationToken ct)
    {
        var csv = await service.GenerateCsvReportAsync(start, end, type, ct);
        var bytes = Encoding.UTF8.GetBytes("\uFEFF" + csv);
        return File(bytes, "text/csv; charset=utf-8", "warehouse_events.csv");
    }
}
