using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DCF.Tests.Services.ScrapeTestHelpers;

namespace DCF.Tests.Services;

public class ScrapeSchedulerServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static ShowEntity CreateShow(bool isExhibition = false, string? url = "https://example.test/recap")
    {
        return new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Show",
            Url = url,
            Date = new DateOnly(2026, 7, 4),
            ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            IsExhibition = isExhibition,
            SeasonId = Guid.NewGuid()
        };
    }

    [Fact]
    public void GetScrapeDelay_AddsDelayMinutesToAnnouncedTime()
    {
        var now = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 10, now);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void GetScrapeDelay_FutureShow_ReturnsPositiveDelay()
    {
        var now = new DateTimeOffset(2025, 7, 1, 20, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 5, now);

        Assert.Equal(TimeSpan.FromMinutes(125), delay);
    }

    [Fact]
    public void GetScrapeDelay_PastShow_ReturnsNegativeDelay()
    {
        var now = new DateTimeOffset(2025, 7, 1, 23, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 5, now);

        Assert.True(delay < TimeSpan.Zero);
    }

    [Fact]
    public void GetScrapeDelay_ZeroDelayMinutes_ReturnsExactTimeToAnnouncement()
    {
        var now = new DateTimeOffset(2025, 7, 1, 20, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 30, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 0, now);

        Assert.Equal(TimeSpan.FromMinutes(150), delay);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_SuccessfulScrape_ReturnsSucceededAndSetsStatus()
    {
        using var db = CreateDb("execute_scrape_success");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask(failuresBeforeSuccess: 0));
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(ScrapeStatus.Succeeded, db.Shows.Single(s => s.Id == show.Id).ScrapeStatus);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ScraperThrows_ReturnsFailedWithErrorAndSetsStatus()
    {
        using var db = CreateDb("execute_scrape_failure");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Failed, result.Outcome);
        Assert.Equal("Simulated scrape failure", result.Error);
        var updated = db.Shows.Single(s => s.Id == show.Id);
        Assert.Equal(ScrapeStatus.Failed, updated.ScrapeStatus);
        Assert.Equal("Simulated scrape failure", updated.ScrapeError);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowIsExhibition_ReturnsSkippedWithoutTouchingStatus()
    {
        using var db = CreateDb("execute_scrape_skipped_exhibition");
        var show = CreateShow(isExhibition: true);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(ScrapeStatus.NotStarted, db.Shows.Single(s => s.Id == show.Id).ScrapeStatus);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowHasNoUrl_ReturnsSkipped()
    {
        using var db = CreateDb("execute_scrape_skipped_no_url");
        var show = CreateShow(url: null);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowDeleted_ReturnsSkipped()
    {
        using var db = CreateDb("execute_scrape_skipped_deleted");
        var show = CreateShow();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_FailsTwiceThenSucceeds_MakesThreeAttemptsAndSucceeds()
    {
        using var db = CreateDb("retry_recovers");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask(failuresBeforeSuccess: 2);
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "5"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, scraperTask.CallCount);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_AlwaysFails_MakesInitialAttemptPlusMaxRetriesAttempts()
    {
        using var db = CreateDb("retry_exhausts");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "3"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Failed, result.Outcome);
        Assert.Equal(4, scraperTask.CallCount);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_ShowSkipped_MakesOnlyOneAttempt()
    {
        using var db = CreateDb("retry_skipped");
        var show = CreateShow(isExhibition: true);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "5"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
        Assert.Equal(0, scraperTask.CallCount);
    }
}
