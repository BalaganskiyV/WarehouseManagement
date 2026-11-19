using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public sealed class WarehouseEventService(
    IRepository<WarehouseEvent> events,
    IRepository<Product> products,
    IRepository<User> users,
    IMemoryCache cache,
    EventCacheVersion version,
    ILogger<WarehouseEventService> logger) : IWarehouseEventService
{
    public static void ValidateFilter(DateTime? start, DateTime? end, string? type)
    {
        if (start > end)
        {
            throw new ArgumentException("start must not be later than end.");
        }

        if (type is not null && type is not ("Arrival" or "Departure" or "Inventory"))
        {
            throw new ArgumentException("Unknown event type.");
        }
    }

    public Task<List<WarehouseEvent>> GetEventsAsync(
        DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default)
    {
        ValidateFilter(start, end, type);
        return events.FindAsync(row =>
            (!start.HasValue || row.EventDate >= start.Value) &&
            (!end.HasValue || row.EventDate <= end.Value) &&
            (type == null || row.EventType == type), ct);
    }

    public Task<WarehouseEvent?> GetEventByIdAsync(int id, CancellationToken ct = default)
    {
        return events.GetByIdAsync(id, ct);
    }

    public async Task<WarehouseEvent> AddEventAsync(
        WarehouseEventInput input, int userId, CancellationToken ct = default)
    {
        var product = await ValidateInputAsync(input, userId, ct);
        var record = new WarehouseEvent
        {
            UserId = userId,
            EventDate = input.EventDate?.UtcDateTime ?? DateTime.UtcNow
        };

        SetDetails(record, input, product);
        await events.AddAsync(record, ct);
        version.Changed();

        logger.LogInformation("Created warehouse event {EventId} by user {UserId}", record.Id, userId);
        return record;
    }

    public async Task<WarehouseEvent?> UpdateEventAsync(
        int id, WarehouseEventInput input, int userId, CancellationToken ct = default)
    {
        var record = await events.GetByIdAsync(id, ct);
        if (record is null)
        {
            return null;
        }

        var product = await ValidateInputAsync(input, userId, ct);
        SetDetails(record, input, product);
        if (input.EventDate.HasValue)
        {
            record.EventDate = input.EventDate.Value.UtcDateTime;
        }

        await events.UpdateAsync(record, ct);
        version.Changed();

        logger.LogInformation("Updated warehouse event {EventId} by user {UserId}", id, userId);
        return record;
    }

    public async Task<bool> DeleteEventAsync(int id, CancellationToken ct = default)
    {
        var record = await events.GetByIdAsync(id, ct);
        if (record is null)
        {
            return false;
        }

        await events.DeleteAsync(record, ct);
        version.Changed();

        logger.LogWarning("Deleted warehouse event {EventId}", id);
        return true;
    }

    public async Task<AnalyticsSummary> GetAnalyticsSummaryAsync(
        DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default)
    {
        ValidateFilter(start, end, type);

        var cacheKey = ("warehouse-analytics", version.Value, start, end, type);
        if (cache.TryGetValue(cacheKey, out AnalyticsSummary? cachedSummary) && cachedSummary is not null)
        {
            return cachedSummary;
        }

        var rows = await GetEventsAsync(start, end, type, ct);
        var byType = rows.GroupBy(row => row.EventType)
            .OrderBy(group => group.Key)
            .Select(group => new TypeSummary(group.Key, group.Count(), group.Sum(row => (long)row.Quantity)))
            .ToArray();
        var byZone = rows.GroupBy(row => row.WarehouseZone)
            .OrderBy(group => group.Key)
            .Select(group => new ZoneSummary(group.Key, group.Count()))
            .ToArray();

        var totalQuantity = rows.Sum(row => (long)row.Quantity);
        var summary = new AnalyticsSummary(rows.Count, totalQuantity, byType, byZone, DateTime.UtcNow);
        cache.Set(cacheKey, summary, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Size = 1
        });

        return summary;
    }

    public async Task<string> GenerateCsvReportAsync(
        DateTime? start = null, DateTime? end = null, string? type = null, CancellationToken ct = default)
    {
        var rows = await GetEventsAsync(start, end, type, ct);
        var csv = new StringBuilder("Id;EventType;ProductName;Quantity;WarehouseZone;EventDate;Description\r\n");
        foreach (var row in rows.OrderBy(row => row.Id))
        {
            var utcDate = DateTime.SpecifyKind(row.EventDate, DateTimeKind.Utc);
            string[] fields =
            [
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.EventType,
                row.ProductName,
                row.Quantity.ToString(CultureInfo.InvariantCulture),
                row.WarehouseZone,
                utcDate.ToString("O", CultureInfo.InvariantCulture),
                row.Description
            ];
            csv.AppendLine(string.Join(';', fields.Select(QuoteCsvField)));
        }

        return csv.ToString();
    }

    private async Task<Product> ValidateInputAsync(WarehouseEventInput input, int userId, CancellationToken ct)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var product = await products.GetByIdAsync(input.ProductId, ct);
        if (product is null)
        {
            throw new ArgumentException("Product does not exist.");
        }

        if (await users.GetByIdAsync(userId, ct) is null)
        {
            throw new ArgumentException("User does not exist.");
        }

        return product;
    }

    private static void SetDetails(WarehouseEvent record, WarehouseEventInput input, Product product)
    {
        record.EventType = input.EventType;
        record.ProductId = product.Id;
        record.ProductName = product.Name;
        record.Quantity = input.Quantity;
        record.WarehouseZone = input.WarehouseZone;
        record.Description = input.Description ?? "";
    }

    private static string QuoteCsvField(string value)
    {
        var trimmed = value.TrimStart();
        var looksLikeFormula = trimmed.Length > 0 && "=+-@".Contains(trimmed[0]);
        if (looksLikeFormula || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n'))
        {
            value = "'" + value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
