using System.Collections.Concurrent;
using DCF.Api.Scraping;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    IMqttPublisherService mqtt,
    IConfiguration config,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private readonly int _delayMinutes = config.GetValue<int>("Scraper:DelayMinutes", 5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var shows = await db.Shows
            .Include(s => s.Season)
            .Include(s => s.ShowCorps)
            .Where(s => s.Season.IsActive && s.ScoresAnnouncedTime > DateTimeOffset.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var show in shows)
        {
            ScheduleScrape(show);
        }
    }

    public void ScheduleScrape(ShowEntity show)
    {
        if (_scheduled.TryRemove(show.Id, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _scheduled[show.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var fireAt = show.ScoresAnnouncedTime.AddMinutes(_delayMinutes);
                var delay = fireAt - DateTimeOffset.UtcNow;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                await ExecuteScrapeAsync(show);

                await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled scrape task failed for show {ShowId}", show.Id);
            }
        });
    }

    public async Task ExecuteScrapeAsync(ShowEntity show)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var freshShow = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == show.Id);

        if (freshShow is null)
        {
            logger.LogWarning("Show {ShowId} not found during scrape", show.Id);

            return;
        }

        var showCorpsIds = freshShow.ShowCorps.Select(sc => sc.CorpsId).ToHashSet();
        var scraperShow = new Show(freshShow.Id, freshShow.Name, freshShow.Url, freshShow.Date);
        var scraper = scope.ServiceProvider.GetRequiredService<IRecapScraperTask>();

        List<Result> results;

        try
        {
            results = await scraper.ScrapeAsync(scraperShow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", freshShow.Id);

            return;
        }

        var scores = results
            .SelectMany(r => EnumerateScores(r))
            .Where(s => showCorpsIds.Contains(s.CorpsId));

        foreach (var score in scores)
        {
            var existing = await db.Scores.FirstOrDefaultAsync(s =>
                s.CorpsId == score.CorpsId &&
                s.ShowId == score.ShowId &&
                s.Caption == score.Caption &&
                s.Judge == score.Judge);

            if (existing is null)
            {
                db.Scores.Add(score);
            }
            else
            {
                existing.TotalScore = score.TotalScore;
                existing.RepertoireScore = score.RepertoireScore;
                existing.PerformanceScore = score.PerformanceScore;
                existing.TotalRank = score.TotalRank;
            }
        }

        await db.SaveChangesAsync();
    }

    private static IEnumerable<ScoreEntity> EnumerateScores(Result r)
    {
        Score?[] scores =
        [
            r.GeneralEffect, r.GeneralEffectMusic1, r.GeneralEffectMusic2,
            r.GeneralEffectVisual1, r.GeneralEffectVisual2,
            r.VisualAnalysis, r.VisualProficiency, r.ColorGuard, r.Visual,
            r.Brass, r.MusicAnalysis, r.Percussion1, r.Percussion2, r.Music,
            r.SubTotal, r.Penalty, r.Total
        ];

        return scores
            .OfType<Score>()
            .Select(s => new ScoreEntity
            {
                Id = s.Id,
                CorpsId = r.Corps.Id,
                ShowId = r.Show.Id,
                Caption = s.Caption,
                Judge = s.Judge,
                RepertoireScore = s.RepertoireScore,
                PerformanceScore = s.PerformanceScore,
                TotalScore = s.TotalScore,
                RepertoireRank = s.RepertoireRank,
                PerformanceRank = s.PerformanceRank,
                TotalRank = s.TotalRank
            });
    }
}
