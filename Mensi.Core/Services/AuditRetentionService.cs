using Mensi.Core.Data;
using Mensi.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditOptions> options,
    TimeProvider clock,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.RetentionDays <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MensiDbContext>();
                var cutoff = clock.GetUtcNow().AddDays(-options.Value.RetentionDays);
                var deleted = await db.AuditEntries.Where(a => a.At < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Audit retention: {Count} sor törölve", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Az audit retention futása elhasalt");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
