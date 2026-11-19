using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WarehouseManagement.Services;

namespace WarehouseManagement.BackgroundServices;

public sealed class EventGeneratorService(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<EventGeneratorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Generator:Enabled"))
        {
            return;
        }

        var seconds = config.GetValue<int>("Generator:IntervalSeconds", 10);
        if (seconds < 1)
        {
            throw new InvalidOperationException("Generator interval must be positive.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await GenerateOneAsync(stoppingToken);
                }
                catch (Exception error) when (error is not OperationCanceledException ||
                                              !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "Background generation failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task GenerateOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Role.Name == "Admin", ct);

        if (admin is null)
        {
            logger.LogWarning("Background generation skipped: no administrator account");
            return;
        }

        var generator = scope.ServiceProvider.GetRequiredService<EventGenerator>();
        await generator.GenerateAsync(1, admin.Id, ct);
    }
}
