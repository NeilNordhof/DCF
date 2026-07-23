// DCF.Tests/Services/DciPublicServiceTests.cs
using DCF.Api.Controllers;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class DciPublicServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static SeasonEntity Season(int year, SeasonStatus status, bool published = true) => new()
    {
        Id = Guid.NewGuid(), Year = year, Status = status, IsPublished = published,
        StartDate = new DateOnly(year, 6, 1), EndDate = new DateOnly(year, 8, 15)
    };

    [Fact]
    public async Task GetCurrentSeasonAsync_ActiveSeasonExists_ReturnsIt()
    {
        using var db = CreateDb("current_season_active");
        var active = Season(2026, SeasonStatus.Active);
        db.Seasons.AddRange(Season(2025, SeasonStatus.Completed), active);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetCurrentSeasonAsync();

        Assert.NotNull(result);
        Assert.Equal(active.Id, result.Id);
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_NoActive_FallsBackToMostRecentCompleted()
    {
        using var db = CreateDb("current_season_completed_fallback");
        var older = Season(2024, SeasonStatus.Completed);
        var newer = Season(2025, SeasonStatus.Completed);
        db.Seasons.AddRange(older, newer, Season(2027, SeasonStatus.Upcoming));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetCurrentSeasonAsync();

        Assert.NotNull(result);
        Assert.Equal(newer.Id, result.Id);
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_NoActiveOrCompleted_FallsBackToMostRecentUpcoming()
    {
        using var db = CreateDb("current_season_upcoming_fallback");
        var upcoming = Season(2026, SeasonStatus.Upcoming);
        db.Seasons.Add(upcoming);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetCurrentSeasonAsync();

        Assert.NotNull(result);
        Assert.Equal(upcoming.Id, result.Id);
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_UnpublishedSeasonsExcludedAtEveryTier()
    {
        using var db = CreateDb("current_season_unpublished");
        db.Seasons.AddRange(
            Season(2026, SeasonStatus.Active, published: false),
            Season(2025, SeasonStatus.Completed, published: false),
            Season(2024, SeasonStatus.Upcoming, published: false));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetCurrentSeasonAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_NoSeasonsAtAll_ReturnsNull()
    {
        using var db = CreateDb("current_season_empty");
        var service = new DciPublicService(db);

        var result = await service.GetCurrentSeasonAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentSeason_Controller_NoSeasons_ReturnsNotFound()
    {
        using var db = CreateDb("current_season_controller_none");
        var service = new DciPublicService(db);
        var controller = new PublicDciController(service);

        var result = await controller.GetCurrentSeason();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetCurrentSeason_Controller_SeasonExists_ReturnsOkWithDto()
    {
        using var db = CreateDb("current_season_controller_ok");
        var active = Season(2026, SeasonStatus.Active);
        db.Seasons.Add(active);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);
        var controller = new PublicDciController(service);

        var result = await controller.GetCurrentSeason();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<DciSeasonDto>(ok.Value);
        Assert.Equal(active.Id, dto.Id);
        Assert.Equal(2026, dto.Year);
    }

    private static (SeasonEntity Season, CorpsEntity Corps) SeedSeasonAndCorps(DcfDbContext db, string corpsName = "Blue Devils")
    {
        var season = Season(2026, SeasonStatus.Active);
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = corpsName };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        return (season, corps);
    }

    private static ShowEntity Show(SeasonEntity season, string name, DateOnly date) => new()
    {
        Id = Guid.NewGuid(), Name = name, Date = date, SeasonId = season.Id, Season = season
    };

    private static ScoreEntity TotalScore(CorpsEntity corps, ShowEntity show, double total, string? judge = null) => new()
    {
        Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.Total, Judge = judge, TotalScore = total
    };

    [Fact]
    public async Task GetStandingsAsync_ComputesLatestAndLast3Avg()
    {
        using var db = CreateDb("standings_latest_last3");
        var (season, corps) = SeedSeasonAndCorps(db);
        var show1 = Show(season, "Show 1", new DateOnly(2026, 7, 1));
        var show2 = Show(season, "Show 2", new DateOnly(2026, 7, 8));
        var show3 = Show(season, "Show 3", new DateOnly(2026, 7, 15));
        db.Shows.AddRange(show1, show2, show3);
        db.Scores.AddRange(TotalScore(corps, show1, 90.0), TotalScore(corps, show2, 92.0), TotalScore(corps, show3, 95.0));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetStandingsAsync(season.Id);

        var entry = Assert.Single(result);
        Assert.Equal(95.0, entry.Latest.Score);
        Assert.Equal("Show 3", entry.Latest.ShowName);
        Assert.Equal(3, entry.Last3.Count);
        Assert.Equal(92.333, Math.Round(entry.Last3Avg, 3));
    }

    [Fact]
    public async Task GetStandingsAsync_MoreThan3Shows_OnlyAveragesMostRecent3()
    {
        using var db = CreateDb("standings_more_than_3");
        var (season, corps) = SeedSeasonAndCorps(db);
        var shows = Enumerable.Range(1, 4)
            .Select(i => Show(season, $"Show {i}", new DateOnly(2026, 7, i * 7)))
            .ToList();
        db.Shows.AddRange(shows);
        db.Scores.AddRange(
            TotalScore(corps, shows[0], 80.0), TotalScore(corps, shows[1], 90.0),
            TotalScore(corps, shows[2], 91.0), TotalScore(corps, shows[3], 92.0));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetStandingsAsync(season.Id);

        var entry = Assert.Single(result);
        Assert.Equal(91.0, Math.Round(entry.Last3Avg, 3));
    }

    [Fact]
    public async Task GetStandingsAsync_CorpsWithNoScoresYet_ExcludedFromResults()
    {
        using var db = CreateDb("standings_no_scores");
        var (season, scoredCorps) = SeedSeasonAndCorps(db, "Scored Corps");
        var unscoredCorps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Unscored Corps" };
        db.Corps.Add(unscoredCorps);
        var show = Show(season, "Show 1", new DateOnly(2026, 7, 1));
        db.Shows.Add(show);
        db.Scores.Add(TotalScore(scoredCorps, show, 90.0));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetStandingsAsync(season.Id);

        var entry = Assert.Single(result);
        Assert.Equal("Scored Corps", entry.CorpsName);
    }

    [Fact]
    public async Task GetStandingsAsync_OnlyNonTotalCaptionScores_CorpsExcluded()
    {
        using var db = CreateDb("standings_only_subcaptions");
        var (season, corps) = SeedSeasonAndCorps(db);
        var show = Show(season, "Show 1", new DateOnly(2026, 7, 1));
        db.Shows.Add(show);
        db.Scores.Add(new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.Brass, TotalScore = 18.0 });
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetStandingsAsync(season.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStandingsAsync_ResultsSortedByLatestScoreDescending()
    {
        using var db = CreateDb("standings_default_sort");
        var season = Season(2026, SeasonStatus.Active);
        var corpsA = new CorpsEntity { Id = Guid.NewGuid(), Name = "Lower Score" };
        var corpsB = new CorpsEntity { Id = Guid.NewGuid(), Name = "Higher Score" };
        db.Seasons.Add(season);
        db.Corps.AddRange(corpsA, corpsB);
        var show = Show(season, "Show 1", new DateOnly(2026, 7, 1));
        db.Shows.Add(show);
        db.Scores.AddRange(TotalScore(corpsA, show, 85.0), TotalScore(corpsB, show, 95.0));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetStandingsAsync(season.Id);

        Assert.Equal("Higher Score", result[0].CorpsName);
        Assert.Equal("Lower Score", result[1].CorpsName);
    }

    [Fact]
    public async Task GetScheduleAsync_OnlyFutureShows_OrderedAscending()
    {
        using var db = CreateDb("schedule_future_only");
        var season = Season(2026, SeasonStatus.Active);
        db.Seasons.Add(season);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var past = Show(season, "Past Show", today.AddDays(-1));
        var soon = Show(season, "Soon Show", today.AddDays(5));
        var later = Show(season, "Later Show", today.AddDays(10));
        db.Shows.AddRange(past, later, soon);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScheduleAsync(season.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal("Soon Show", result[0].Name);
        Assert.Equal("Later Show", result[1].Name);
    }

    [Fact]
    public async Task GetScheduleAsync_IncludesScheduleEntriesOrderedBySortOrder_TbdTimeAllowed()
    {
        using var db = CreateDb("schedule_entries");
        var season = Season(2026, SeasonStatus.Active);
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var show = Show(season, "Upcoming Show", today.AddDays(3));
        db.Shows.Add(show);
        db.ShowScheduleEntries.AddRange(
            new ShowScheduleEntryEntity { Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 1, Label = "Blue Devils", CorpsId = corps.Id, Time = null },
            new ShowScheduleEntryEntity { Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 0, Label = "Gates Open", CorpsId = null, Time = new DateTimeOffset(2026, 7, 25, 18, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScheduleAsync(season.Id);

        var entries = Assert.Single(result).Schedule;
        Assert.Equal("Gates Open", entries[0].Label);
        Assert.Equal("Blue Devils", entries[1].Label);
        Assert.Null(entries[1].Time);
        Assert.Equal("Blue Devils", entries[1].CorpsName);
    }

    [Fact]
    public async Task GetScoresAsync_OnlyPastShows_OrderedDescending()
    {
        using var db = CreateDb("scores_past_only");
        var season = Season(2026, SeasonStatus.Active);
        db.Seasons.Add(season);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var future = Show(season, "Future Show", today.AddDays(1));
        var older = Show(season, "Older Show", today.AddDays(-10));
        var recent = Show(season, "Recent Show", today.AddDays(-1));
        db.Shows.AddRange(future, older, recent);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScoresAsync(season.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal("Recent Show", result[0].Name);
        Assert.Equal("Older Show", result[1].Name);
    }

    [Fact]
    public async Task GetScoresAsync_HasTotalScores_RanksDescendingByTotal()
    {
        using var db = CreateDb("scores_ranked_results");
        var season = Season(2026, SeasonStatus.Active);
        var corpsA = new CorpsEntity { Id = Guid.NewGuid(), Name = "Second Place" };
        var corpsB = new CorpsEntity { Id = Guid.NewGuid(), Name = "First Place" };
        db.Seasons.Add(season);
        db.Corps.AddRange(corpsA, corpsB);
        var show = Show(season, "Show 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        db.Shows.Add(show);
        db.Scores.AddRange(TotalScore(corpsA, show, 90.0), TotalScore(corpsB, show, 95.0));
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScoresAsync(season.Id);

        var scoresShow = Assert.Single(result);
        Assert.False(scoresShow.ScoresPending);
        Assert.Null(scoresShow.NoScoreReason);
        Assert.Equal(1, scoresShow.Results[0].Rank);
        Assert.Equal("First Place", scoresShow.Results[0].CorpsName);
        Assert.Equal(2, scoresShow.Results[1].Rank);
        Assert.Equal("Second Place", scoresShow.Results[1].CorpsName);
    }

    [Fact]
    public async Task GetScoresAsync_NoScoreReasonSet_ReturnsReasonNotResults()
    {
        using var db = CreateDb("scores_no_score_reason");
        var season = Season(2026, SeasonStatus.Active);
        db.Seasons.Add(season);
        var show = Show(season, "Rained Out Show", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        show.NoScoreReason = "Rained out";
        db.Shows.Add(show);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScoresAsync(season.Id);

        var scoresShow = Assert.Single(result);
        Assert.Equal("Rained out", scoresShow.NoScoreReason);
        Assert.False(scoresShow.ScoresPending);
        Assert.Empty(scoresShow.Results);
    }

    [Fact]
    public async Task GetScoresAsync_PastShowNoTotalsNoReason_MarkedScoresPending()
    {
        using var db = CreateDb("scores_pending");
        var season = Season(2026, SeasonStatus.Active);
        db.Seasons.Add(season);
        var show = Show(season, "Just Happened Show", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        db.Shows.Add(show);
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetScoresAsync(season.Id);

        var scoresShow = Assert.Single(result);
        Assert.True(scoresShow.ScoresPending);
        Assert.Null(scoresShow.NoScoreReason);
        Assert.Empty(scoresShow.Results);
    }

    [Fact]
    public async Task GetRecapAsync_UnknownShow_ReturnsNull()
    {
        using var db = CreateDb("recap_unknown_show");
        var service = new DciPublicService(db);

        var result = await service.GetRecapAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecapAsync_ReturnsShowMetadataAndAllScoreRowsGroupedByCorps()
    {
        using var db = CreateDb("recap_basic");
        var season = Season(2026, SeasonStatus.Active);
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        var show = Show(season, "DCI Southwestern", new DateOnly(2026, 7, 20));
        show.Location = "Denver, CO";
        db.Shows.Add(show);
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.Brass, Judge = "P. McGarr", RepertoireScore = 9.5, PerformanceScore = 9.6, TotalScore = 19.1 },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.Total, TotalScore = 96.85 });
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetRecapAsync(show.Id);

        Assert.NotNull(result);
        Assert.Equal("DCI Southwestern", result.Show.Name);
        Assert.Equal("Denver, CO", result.Show.Location);
        var corpsEntry = Assert.Single(result.Corps);
        Assert.Equal("Blue Devils", corpsEntry.CorpsName);
        Assert.Equal(2, corpsEntry.Scores.Count);
        var brassRow = corpsEntry.Scores.Single(s => s.Caption == Caption.Brass);
        Assert.Equal("P. McGarr", brassRow.Judge);
        Assert.Equal(9.5, brassRow.RepertoireScore);
    }

    [Fact]
    public async Task GetRecapAsync_FullPanelCaptionWithTwoJudges_BothRowsReturnedUncollapsed()
    {
        using var db = CreateDb("recap_two_judges");
        var season = Season(2026, SeasonStatus.Active);
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        var show = Show(season, "DCI World Championships", new DateOnly(2026, 8, 15));
        db.Shows.Add(show);
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.GeneralEffectVisual, Judge = "Judge A", TotalScore = 19.4 },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id, Caption = Caption.GeneralEffectVisual, Judge = "Judge B", TotalScore = 19.2 });
        await db.SaveChangesAsync();
        var service = new DciPublicService(db);

        var result = await service.GetRecapAsync(show.Id);

        var corpsEntry = Assert.Single(result!.Corps);
        var geVisualRows = corpsEntry.Scores.Where(s => s.Caption == Caption.GeneralEffectVisual).ToList();
        Assert.Equal(2, geVisualRows.Count);
        Assert.Contains(geVisualRows, r => r.Judge == "Judge A" && r.TotalScore == 19.4);
        Assert.Contains(geVisualRows, r => r.Judge == "Judge B" && r.TotalScore == 19.2);
    }
}
