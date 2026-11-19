using Application.DTOs;
using Domain.Entities;
namespace Application.Interfaces;
public interface IWarehouseEventService
{
    Task<List<WarehouseEvent>> GetEventsAsync(DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default);
    Task<WarehouseEvent?> GetEventByIdAsync(int id, CancellationToken ct = default);
    Task<WarehouseEvent> AddEventAsync(WarehouseEventInput input, int userId, CancellationToken ct = default);
    Task<WarehouseEvent?> UpdateEventAsync(int id, WarehouseEventInput input, int userId, CancellationToken ct = default);
    Task<bool> DeleteEventAsync(int id, CancellationToken ct = default);
    Task<string> GenerateCsvReportAsync(DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default);
    Task<AnalyticsSummary> GetAnalyticsSummaryAsync(DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default);
}
