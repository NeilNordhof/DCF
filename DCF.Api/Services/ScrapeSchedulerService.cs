using System.Collections.Concurrent;
using DCF.Api.Scraping;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DCF.Api.Services;

public enum ScrapeOutcome { Succeeded, Failed, Skipped }

public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    IMqttService mqtt,
    IConfiguration config,
    IOptions<EmailOptions> emailOptions,
    EmailTokenService emailTokenService,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private readonly int _delayMinutes = config.GetValue<int>("Scraper:DelayMinutes", 5);
    private readonly int _maxRetries = config.GetValue<int>("Scraper:MaxRetries", 5);
    private readonly int _retryIntervalMinutes = config.GetValue<int>("Scraper:RetryIntervalMinutes", 5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var shows = await db.Shows
            .Include(s => s.ShowCorps)
            .Where(s => !s.IsExhibition
                     && s.Url != null
                     && s.ScoresAnnouncedTime.HasValue
                     && s.ScoresAnnouncedTime.Value > DateTimeOffset.UtcNow
                     && s.NoScoreReason == null)
            .ToListAsync(stoppingToken);

        foreach (var show in shows)
        {
            ScheduleScrape(show);
        }
    }

    public void ScheduleScrape(ShowEntity show)
    {
        if (show.IsExhibition || show.Url is null || show.ScoresAnnouncedTime is null || show.NoScoreReason != null)
        {
            return;
        }

        CancelScheduledScrape(show.Id);

        var cts = new CancellationTokenSource();
        _scheduled[show.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = GetScrapeDelay(show.ScoresAnnouncedTime.Value, _delayMinutes, DateTimeOffset.UtcNow);

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                await ExecuteScrapeWithRetriesAsync(show, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled
            }
            catch (ObjectDisposedException)
            {
                // expected when cancelled before the delay/token registration was reached
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled scrape task failed for show {ShowId}", show.Id);
            }
        });
    }

    public void CancelScheduledScrape(Guid showId)
    {
        if (_scheduled.TryRemove(showId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
    }

    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeWithRetriesAsync(ShowEntity show, CancellationToken token)
    {
        var result = await ExecuteScrapeAsync(show);

        var retry = 0;

        // _maxRetries counts retries after the initial attempt above, so this loop
        // runs at most _maxRetries additional times (1 + _maxRetries attempts total).
        while (result.Outcome == ScrapeOutcome.Failed && retry < _maxRetries)
        {
            await Task.Delay(TimeSpan.FromMinutes(_retryIntervalMinutes), token);

            result = await ExecuteScrapeAsync(show);

            retry++;
        }

        if (result.Outcome == ScrapeOutcome.Failed)
        {
            await SendScrapeFailedAlertAsync(show, result.Error);
        }

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });

        return result;
    }

    public static TimeSpan GetScrapeDelay(DateTimeOffset scoresAnnouncedTime, int delayMinutes, DateTimeOffset now)
        => scoresAnnouncedTime.AddMinutes(delayMinutes) - now;

    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeAsync(ShowEntity show)
    {
        using var _ = logger.BeginScope(new Dictionary<string, object> { ["ShowId"] = show.Id });
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var freshShow = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == show.Id);

        if (freshShow is null || freshShow.IsExhibition || freshShow.Url is null)
        {
            logger.LogWarning("Show {ShowId} cannot be scraped", show.Id);

            return (ScrapeOutcome.Skipped, null);
        }

        var showCorpsIds = freshShow.ShowCorps.Select(sc => sc.CorpsId).ToHashSet();
        var scraperShow = new Show(freshShow.Id, freshShow.Name, freshShow.Url, freshShow.Date);
        var scraper = scope.ServiceProvider.GetRequiredService<IRecapScraperTask>();

        freshShow.LastScrapeAttemptAt = DateTimeOffset.UtcNow;

        List<Result> results;

        try
        {
            results = await scraper.ScrapeAsync(scraperShow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", freshShow.Id);

            freshShow.ScrapeStatus = ScrapeStatus.Failed;
            freshShow.ScrapeError = ex.Message;

            await db.SaveChangesAsync();

            return (ScrapeOutcome.Failed, ex.Message);
        }

        freshShow.ScrapeStatus = ScrapeStatus.Succeeded;
        freshShow.ScrapeError = null;

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

        await ComputeAndUpsertComputedScoresAsync(db, freshShow.Id, freshShow.SeasonId);

        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        await SendScoresUpdatedNotificationsAsync(db, emailService, freshShow.SeasonId, freshShow.Id, freshShow.Name);

        return (ScrapeOutcome.Succeeded, null);
    }

    private async Task SendScoresUpdatedNotificationsAsync(DcfDbContext db, IEmailService emailService, Guid seasonId, Guid showId, string showName)
    {
        try
        {
            var leagueIds = await db.Leagues
                .Where(l => l.SeasonId == seasonId && l.DraftStatus == DraftStatus.Completed)
                .Select(l => l.Id)
                .ToListAsync();

            if (leagueIds.Count == 0)
            {
                return;
            }

            var users = await db.LeagueMembers
                .Include(m => m.User)
                .Where(m => leagueIds.Contains(m.LeagueId) && m.User.EmailNotificationsEnabled)
                .Select(m => m.User)
                .Distinct()
                .ToListAsync();

            var totals = await db.Scores
                .Where(s => s.ShowId == showId && s.Caption == Caption.Total)
                .OrderByDescending(s => s.TotalScore)
                .Select(s => new { s.Corps.Name, s.TotalScore })
                .ToListAsync();

            var scoreRows = totals
                .Select((r, i) => new EmailScoreRow(i + 1, r.Name, r.TotalScore))
                .ToList();

            foreach (var user in users)
            {
                var token = emailTokenService.GenerateToken(user.Id);
                var (subject, html) = EmailTemplate.ScoresAvailable(showName, showId, scoreRows, emailOptions.Value.FrontendUrl, token);

                await emailService.SendAsync(user.Email, user.DisplayName, subject, html);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send scores-updated notifications for show {ShowName}", showName);
        }
    }

    private async Task SendScrapeFailedAlertAsync(ShowEntity show, string? error)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var admins = await db.Users
                .Where(u => u.IsAdmin && u.EmailNotificationsEnabled)
                .ToListAsync();

            foreach (var admin in admins)
            {
                var token = emailTokenService.GenerateToken(admin.Id);
                var (subject, html) = EmailTemplate.ScrapeFailed(show.Name, error ?? "Unknown error", show.SeasonId, emailOptions.Value.FrontendUrl, token);

                await emailService.SendAsync(admin.Email, admin.DisplayName, subject, html);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send scrape-failed alert for show {ShowId}", show.Id);
        }
    }

    public static async Task ComputeAndUpsertComputedScoresAsync(DcfDbContext db, Guid showId, Guid seasonId)
    {
        var showScores = await db.Scores
            .Where(s => s.ShowId == showId)
            .ToListAsync();

        var byCorps = showScores.GroupBy(s => s.CorpsId);

        foreach (var group in byCorps)
        {
            var corpsId = group.Key;
            var scores = group.ToList();

            double Avg(Caption caption)
            {
                var vals = scores.Where(s => s.Caption == caption).Select(s => s.TotalScore).ToList();
                return vals.Count > 0 ? vals.Average() : 0;
            }

            double Single(Caption caption)
            {
                return scores.FirstOrDefault(s => s.Caption == caption)?.TotalScore ?? 0;
            }

            var ge1 = Avg(Caption.GeneralEffectVisual);
            var ge2 = Avg(Caption.GeneralEffectMusic);
            var vp = Single(Caption.VisualProficiency);
            var va = Single(Caption.VisualAnalysis);
            var cg = Single(Caption.ColorGuard);
            var brass = Single(Caption.Brass);
            var perc = Single(Caption.Percussion);
            var ma = Avg(Caption.MusicAnalysis);

            var existing = await db.ComputedScores
                .FirstOrDefaultAsync(cs => cs.ShowId == showId && cs.CorpsId == corpsId);

            if (existing is null)
            {
                db.ComputedScores.Add(new ComputedScoreEntity
                {
                    Id = Guid.NewGuid(),
                    ShowId = showId,
                    SeasonId = seasonId,
                    CorpsId = corpsId,
                    GeneralEffect1 = ge1,
                    GeneralEffect2 = ge2,
                    GeneralEffectCombined = ge1 + ge2,
                    Visual = (vp + va) / 2,
                    VisualCombined = (vp + va + cg) / 2,
                    Colorguard = cg,
                    VisualProficiency = vp,
                    VisualAnalysis = va,
                    Brass = brass,
                    Percussion = perc,
                    MusicAnalysis = ma,
                    MusicCombined = (brass + ma + perc) / 2
                });
            }
            else
            {
                existing.GeneralEffect1 = ge1;
                existing.GeneralEffect2 = ge2;
                existing.GeneralEffectCombined = ge1 + ge2;
                existing.Visual = (vp + va) / 2;
                existing.VisualCombined = (vp + va + cg) / 2;
                existing.Colorguard = cg;
                existing.VisualProficiency = vp;
                existing.VisualAnalysis = va;
                existing.Brass = brass;
                existing.Percussion = perc;
                existing.MusicAnalysis = ma;
                existing.MusicCombined = (brass + ma + perc) / 2;
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
            r.Brass, r.MusicAnalysis1, r.Percussion, r.MusicAnalysis2, r.Music,
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
