using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class SeasonStatusService(
    IServiceScopeFactory scopeFactory,
    ILogger<SeasonStatusService> logger) : BackgroundService, ISeasonStatusService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();

    private static readonly TimeSpan _maxDelayChunk = TimeSpan.FromDays(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var seasons = await db.Seasons
            .Where(s => s.Status != SeasonStatus.Completed)
            .ToListAsync(stoppingToken);

        var statusesBefore = seasons.ToDictionary(s => s.Id, s => s.Status);

        ApplyStatusTransitions(seasons, today);

        foreach (var season in seasons.Where(s => s.Status != statusesBefore[s.Id]))
        {
            logger.LogInformation("Season {Year} set to {Status} on startup", season.Year, season.Status);
        }

        await db.SaveChangesAsync(stoppingToken);

        foreach (var season in seasons.Where(s => s.Status != SeasonStatus.Completed))
        {
            ScheduleSeason(season);
        }
    }

    public static void ApplyStatusTransitions(IList<SeasonEntity> seasons, DateOnly today)
    {
        foreach (var season in seasons)
        {
            if (season.Status == SeasonStatus.Active && season.EndDate < today)
            {
                season.Status = SeasonStatus.Completed;
            }
            else if (season.Status == SeasonStatus.Upcoming && season.StartDate <= today)
            {
                season.Status = SeasonStatus.Active;
            }
        }
    }

    public void ScheduleSeason(SeasonEntity season)
    {
        if (_scheduled.TryRemove(season.Id, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        if (season.Status == SeasonStatus.Completed)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _scheduled[season.Id] = cts;

        _ = Task.Run(async () =>
        {
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["SeasonId"] = season.Id });

            try
            {
                if (season.Status == SeasonStatus.Upcoming)
                {
                    var activateAt = season.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

                    await DelayUntilAsync(activateAt, cts.Token);

                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    using (var scope = scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

                        await db.Seasons
                            .Where(s => s.Id == season.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SeasonStatus.Active));

                        logger.LogInformation("Season {Year} status set to Active", season.Year);
                    }
                }

                var completeAt = season.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

                await DelayUntilAsync(completeAt, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                using (var scope = scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

                    await db.Seasons
                        .Where(s => s.Id == season.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SeasonStatus.Completed));

                    logger.LogInformation("Season {Year} status set to Completed", season.Year);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SeasonStatusService task failed for season {SeasonId}", season.Id);
            }
        });
    }

    private static async Task DelayUntilAsync(DateTime target, CancellationToken ct)
    {
        while (true)
        {
            var remaining = target - DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var chunk = remaining < _maxDelayChunk ? remaining : _maxDelayChunk;

            await Task.Delay(chunk, ct);
        }
    }
}
