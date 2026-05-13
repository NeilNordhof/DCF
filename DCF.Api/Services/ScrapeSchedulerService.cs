using DCF.Data;
using DCF.Data.Entities;
using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Services;
using DCF.ScoreScraper.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    IMqttPublisherService mqtt,
    IConfiguration config,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, CancellationTokenSource> _scheduled = new();
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
            ScheduleScrape(show);
    }

    public void ScheduleScrape(ShowEntity show)
    {
        if (_scheduled.TryGetValue(show.Id, out var existing))
        {
            existing.Cancel();
            _scheduled.Remove(show.Id);
        }

        var cts = new CancellationTokenSource();
        _scheduled[show.Id] = cts;

        _ = Task.Run(async () =>
        {
            var fireAt = show.ScoresAnnouncedTime.AddMinutes(_delayMinutes);
            var delay = fireAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            await ExecuteScrapeAsync(show);
            await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
        }, cts.Token);
    }

    public async Task ExecuteScrapeAsync(ShowEntity show)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var showCorpsIds = show.ShowCorps.Select(sc => sc.CorpsId).ToHashSet();
        var corpsList = await db.Corps
            .Where(c => showCorpsIds.Contains(c.Id))
            .ToListAsync();

        var scraperCorps = corpsList.Select(c => new Corps(c.Id, c.Name));
        var scraperShow = new Show(show.Id, show.Name, show.Url, show.Date);

        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var scraper = new RecapScraperTask(
            new CorpsService(scraperCorps),
            httpClientFactory.CreateClient());

        List<DCF.ScoreScraper.Models.Result> results;
        try
        {
            results = await scraper.ScrapeAsync(scraperShow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", show.Id);
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
                db.Scores.Add(score);
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

    private static IEnumerable<ScoreEntity> EnumerateScores(DCF.ScoreScraper.Models.Result r)
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
