using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class DraftSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<DraftSchedulerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private static readonly TimeSpan OpenLeadTime = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var leagues = await db.Leagues
            .Where(l => (l.DraftStatus == DraftStatus.Scheduled || l.DraftStatus == DraftStatus.Open)
                        && l.DraftStartTime != null)
            .ToListAsync(stoppingToken);

        foreach (var league in leagues)
        {
            ScheduleNext(league.Id, league.DraftStartTime!.Value, league.DraftStatus == DraftStatus.Open);
        }
    }

    public void ScheduleNext(Guid leagueId, DateTimeOffset startTime, bool isAlreadyOpened)
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
                if (!isAlreadyOpened)
                {
                    var openDelay = startTime - OpenLeadTime - DateTimeOffset.UtcNow;

                    if (openDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(openDelay, cts.Token);
                    }

                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        using var openScope = scopeFactory.CreateScope();
                        var openService = openScope.ServiceProvider.GetRequiredService<IDraftService>();

                        await openService.OpenDraftAsync(leagueId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        logger.LogWarning("Scheduled draft could not open for league {Id}: {Message}", leagueId, ex.Message);

                        using var resetScope = scopeFactory.CreateScope();
                        var db = resetScope.ServiceProvider.GetRequiredService<DcfDbContext>();
                        var league = await db.Leagues.FindAsync(leagueId);

                        if (league is not null)
                        {
                            league.DraftStatus = DraftStatus.NotStarted;
                            league.DraftStartTime = null;
                            league.IssueMessages = [.. league.IssueMessages, ex.Message];

                            await db.SaveChangesAsync();
                        }

                        return;
                    }
                }

                var startDelay = startTime - DateTimeOffset.UtcNow;

                if (startDelay > TimeSpan.Zero)
                {
                    await Task.Delay(startDelay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                using var startScope = scopeFactory.CreateScope();
                var startService = startScope.ServiceProvider.GetRequiredService<IDraftService>();

                await startService.StartDraftAsync(leagueId);
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled or cancelled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Draft scheduling failed for league {Id}", leagueId);
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
