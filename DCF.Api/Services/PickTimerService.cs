using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class PickTimerService(
    IServiceScopeFactory scopeFactory,
    ILogger<PickTimerService> logger) : BackgroundService, IPickTimerService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var leagues = await db.Leagues
            .Where(l => l.DraftStatus == DraftStatus.InProgress && l.PickDeadline != null)
            .ToListAsync(stoppingToken);

        foreach (var league in leagues)
        {
            SchedulePickExpiration(league.Id, league.CurrentPickNumber, league.PickDeadline!.Value);
        }
    }

    public void SchedulePickExpiration(Guid leagueId, int pickNumber, DateTimeOffset deadline)
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
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["LeagueId"] = leagueId });

            try
            {
                var delay = deadline - DateTimeOffset.UtcNow;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var draftService = scope.ServiceProvider.GetRequiredService<IDraftService>();

                await draftService.ExpireCurrentPickAsync(leagueId, pickNumber);
            }
            catch (OperationCanceledException)
            {
                // expected when a pick is submitted/skipped before the timer expires
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pick timer expiration failed for league {Id}", leagueId);
            }
            finally
            {
                // Only clean up our own entry - a newer SchedulePickExpiration/CancelPickTimer
                // call for this league may have already replaced (and disposed) it while we
                // were running, in which case this is a no-op.
                if (_scheduled.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(leagueId, cts)))
                {
                    cts.Dispose();
                }
            }
        });
    }

    public void CancelPickTimer(Guid leagueId)
    {
        if (_scheduled.TryRemove(leagueId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
