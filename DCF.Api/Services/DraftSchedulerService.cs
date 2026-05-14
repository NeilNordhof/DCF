using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class DraftSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<DraftSchedulerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var leagues = await db.Leagues
            .Where(l => l.DraftStatus == DraftStatus.Scheduled &&
                        l.DraftStartTime != null &&
                        l.DraftStartTime > DateTimeOffset.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var league in leagues)
            ScheduleDraftStart(league.Id, league.DraftStartTime!.Value);
    }

    public void ScheduleDraftStart(Guid leagueId, DateTimeOffset startTime)
    {
        if (_scheduled.TryRemove(leagueId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _scheduled[leagueId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = startTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                using var scope = scopeFactory.CreateScope();
                var draftService = scope.ServiceProvider.GetRequiredService<DraftService>();
                await draftService.StartDraftAsync(leagueId);
            }
            catch (OperationCanceledException) { /* expected when rescheduled */ }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-start draft failed for league {Id}", leagueId);
            }
        });
    }

    public void CancelScheduled(Guid leagueId)
    {
        if (_scheduled.TryRemove(leagueId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
