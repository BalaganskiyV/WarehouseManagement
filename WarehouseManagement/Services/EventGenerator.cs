using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;

namespace WarehouseManagement.Services;

public sealed class EventGenerator(IRepository<Product> products, IWarehouseEventService events)
{
    private static readonly string[] EventTypes = ["Arrival", "Departure", "Inventory"];
    private static readonly string[] Zones = ["A-1", "A-2", "B-1", "B-2", "C-1", "C-3"];

    public async Task<int> GenerateAsync(int count, int userId, CancellationToken ct)
    {
        if (count < 1 || count > 1000)
        {
            throw new ArgumentException("count must be between 1 and 1000.");
        }

        var availableProducts = await products.GetAllAsync(ct);
        if (availableProducts.Count == 0)
        {
            throw new ArgumentException("Create products before generating events.");
        }

        var random = Random.Shared;
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var product = availableProducts[random.Next(availableProducts.Count)];
            var input = new WarehouseEventInput
            {
                EventType = EventTypes[random.Next(EventTypes.Length)],
                ProductId = product.Id,
                Quantity = random.Next(1, 51),
                WarehouseZone = Zones[random.Next(Zones.Length)],
                Description = "Автоматически сгенерированное событие"
            };

            await events.AddEventAsync(input, userId, ct);
        }

        return count;
    }
}
