using DCF.Api.Models;
using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DCF.Tests.Services.ScrapeTestHelpers;

namespace DCF.Tests.Services;

public class AdminServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private class NoOpSeasonStatus : ISeasonStatusService
    {
        public void ScheduleSeason(SeasonEntity season)
        {
        }
    }

    [Fact]
    public async Task CreateSeasonAsync_PersistsSeasonWithCorrectFields()
    {
        using var db = CreateDb("admin_create_season");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 8, 12);
        var result = await svc.CreateSeasonAsync(2026, start, end);

        Assert.Equal(2026, result.Year);
        Assert.Equal(start, result.StartDate);
        Assert.Equal(end, result.EndDate);
        Assert.Equal(SeasonStatus.Upcoming, result.Status);
        Assert.False(result.IsPublished);
    }

    [Fact]
    public async Task GetSeasonDetailAsync_MissingSeason_ReturnsNull()
    {
        using var db = CreateDb("admin_get_detail_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var result = await svc.GetSeasonDetailAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSeasonDetailAsync_ExistingSeason_ReturnsDetailWithCorpsIds()
    {
        using var db = CreateDb("admin_get_detail_existing");
        var seasonId = Guid.NewGuid();
        var corps1Id = Guid.NewGuid();
        var corps2Id = Guid.NewGuid();

        db.Seasons.Add(new SeasonEntity
        {
            Id = seasonId,
            Year = 2026,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 12)
        });
        db.SeasonCorps.AddRange(
        [
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = corps1Id },
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = corps2Id }
        ]);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.GetSeasonDetailAsync(seasonId);

        Assert.NotNull(result);
        Assert.Equal(seasonId, result.Id);
        Assert.Equal(2026, result.Year);
        Assert.Contains(corps1Id, result.CorpsIds);
        Assert.Contains(corps2Id, result.CorpsIds);
    }

    [Fact]
    public async Task PublishSeasonAsync_MissingSeason_ReturnsFalse()
    {
        using var db = CreateDb("admin_publish_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var result = await svc.PublishSeasonAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task PublishSeasonAsync_ExistingSeason_SetsIsPublished()
    {
        using var db = CreateDb("admin_publish_existing");
        var seasonId = Guid.NewGuid();
        db.Seasons.Add(new SeasonEntity
        {
            Id = seasonId,
            Year = 2026,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 12)
        });
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.PublishSeasonAsync(seasonId);

        Assert.True(result);
        var season = await db.Seasons.FindAsync(seasonId);
        Assert.True(season!.IsPublished);
    }

    [Fact]
    public async Task RenameCorps_UpdatesName()
    {
        using var db = CreateDb("corps_rename");
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Old Name" };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.RenameCorpsAsync(corps.Id, "New Name");

        Assert.NotNull(result);
        Assert.Equal("New Name", result!.Name);
        Assert.Equal("New Name", db.Corps.Single(c => c.Id == corps.Id).Name);
    }

    [Fact]
    public async Task RenameCorps_MissingId_ReturnsNull()
    {
        using var db = CreateDb("corps_rename_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var result = await svc.RenameCorpsAsync(Guid.NewGuid(), "Anything");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteCorps_NotInPublishedSeason_DeletesAndReturnsTrue()
    {
        using var db = CreateDb("corps_delete_ok");
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var (found, deletable) = await svc.DeleteCorpsAsync(corps.Id);

        Assert.True(found);
        Assert.True(deletable);
        Assert.False(db.Corps.Any(c => c.Id == corps.Id));
    }

    [Fact]
    public async Task DeleteCorps_InPublishedSeason_ReturnsDeletableFalse()
    {
        using var db = CreateDb("corps_delete_blocked");
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
            IsPublished = true
        };
        db.Corps.Add(corps);
        db.Seasons.Add(season);
        db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps.Id });
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var (found, deletable) = await svc.DeleteCorpsAsync(corps.Id);

        Assert.True(found);
        Assert.False(deletable);
        Assert.True(db.Corps.Any(c => c.Id == corps.Id));
    }

    [Fact]
    public async Task SetCorpsIconAsync_ExistingCorps_UpdatesPathAndReturnsOldPath()
    {
        using var db = CreateDb("corps_icon_update");
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils", IconPath = "corps-icons/old.png" };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var (found, oldPath) = await svc.SetCorpsIconAsync(corps.Id, "corps-icons/new.jpg");

        Assert.True(found);
        Assert.Equal("corps-icons/old.png", oldPath);
        Assert.Equal("corps-icons/new.jpg", db.Corps.Single(c => c.Id == corps.Id).IconPath);
    }

    [Fact]
    public async Task SetCorpsIconAsync_NoExistingIcon_ReturnsNullOldPath()
    {
        using var db = CreateDb("corps_icon_no_existing");
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var (found, oldPath) = await svc.SetCorpsIconAsync(corps.Id, "corps-icons/cav.png");

        Assert.True(found);
        Assert.Null(oldPath);
        Assert.Equal("corps-icons/cav.png", db.Corps.Single(c => c.Id == corps.Id).IconPath);
    }

    [Fact]
    public async Task SetCorpsIconAsync_MissingCorps_ReturnsFalse()
    {
        using var db = CreateDb("corps_icon_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var (found, oldPath) = await svc.SetCorpsIconAsync(Guid.NewGuid(), "corps-icons/x.png");

        Assert.False(found);
        Assert.Null(oldPath);
    }

    [Fact]
    public async Task UpdateSeasonDates_WhenNotPublished_UpdatesDates()
    {
        using var db = CreateDb("season_update_dates");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.UpdateSeasonDatesAsync(season.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 1));

        Assert.True(result);
        var updated = db.Seasons.Single(s => s.Id == season.Id);
        Assert.Equal(new DateOnly(2026, 6, 15), updated.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 1), updated.EndDate);
    }

    [Fact]
    public async Task UpdateSeasonDates_WhenPublished_ReturnsFalse()
    {
        using var db = CreateDb("season_update_dates_published");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
            IsPublished = true
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.UpdateSeasonDatesAsync(season.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 1));

        Assert.False(result);
        Assert.Equal(new DateOnly(2026, 6, 1), db.Seasons.Single(s => s.Id == season.Id).StartDate);
    }

    [Fact]
    public async Task SetSeasonCorpsOrderAsync_UpdatesSortOrders()
    {
        using var db = CreateDb("corps_sort_update");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
        };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Alpha" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Beta" };
        db.Seasons.Add(season);
        db.Corps.AddRange(corps1, corps2);
        db.SeasonCorps.AddRange(
            new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps1.Id },
            new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps2.Id }
        );
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var orders = new List<(Guid CorpsId, int? SortOrder)>
        {
            (corps1.Id, 2),
            (corps2.Id, 1)
        };
        var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(season.Id, orders);

        Assert.True(found);
        Assert.True(canEdit);
        Assert.Equal(2, db.SeasonCorps.Single(sc => sc.CorpsId == corps1.Id).SortOrder);
        Assert.Equal(1, db.SeasonCorps.Single(sc => sc.CorpsId == corps2.Id).SortOrder);
    }

    [Fact]
    public async Task SetSeasonCorpsOrderAsync_PublishedSeason_ReturnsCanEditFalse()
    {
        using var db = CreateDb("corps_sort_published");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
            IsPublished = true
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(season.Id, []);

        Assert.True(found);
        Assert.False(canEdit);
    }

    [Fact]
    public async Task SetSeasonCorpsOrderAsync_MissingSeason_ReturnsFoundFalse()
    {
        using var db = CreateDb("corps_sort_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(Guid.NewGuid(), []);

        Assert.False(found);
        Assert.False(canEdit);
    }

    [Fact]
    public async Task DeleteShow_ExistingShow_DeletesAndReturnsTrue()
    {
        using var db = CreateDb("show_delete");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
        };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Regionals", Url = "https://x",
            Date = new DateOnly(2026, 7, 1),
            ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddDays(30),
            SeasonId = season.Id
        };
        db.Seasons.Add(season);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var result = await svc.DeleteShowAsync(show.Id);

        Assert.True(result);
        Assert.False(db.Shows.Any(s => s.Id == show.Id));
    }

    [Fact]
    public async Task ShowScheduleEntryEntity_CanPersistAndRetrieve()
    {
        using var db = CreateDb("schedule_entity_persist");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = 2030,
            StartDate = new DateOnly(2030, 6, 1),
            EndDate = new DateOnly(2030, 8, 31)
        };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Test Corps" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Show",
            Date = new DateOnly(2030, 7, 4),
            ScoresAnnouncedTime = null,
            IsExhibition = true,
            Location = "Test Venue, City, ST",
            Latitude = 39.7684,
            Longitude = -86.1581,
            SeasonId = season.Id
        };

        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.ShowScheduleEntries.AddRange(
        [
            new ShowScheduleEntryEntity
            {
                Id = Guid.NewGuid(),
                ShowId = show.Id,
                SortOrder = 0,
                Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
                Label = "Test Corps",
                CorpsId = corps.Id
            },
            new ShowScheduleEntryEntity
            {
                Id = Guid.NewGuid(),
                ShowId = show.Id,
                SortOrder = 1,
                Time = new DateTimeOffset(2030, 7, 5, 0, 30, 0, TimeSpan.Zero),
                Label = "Awards",
                CorpsId = null
            }
        ]);

        await db.SaveChangesAsync();

        var entries = db.ShowScheduleEntries
            .Where(e => e.ShowId == show.Id)
            .OrderBy(e => e.SortOrder)
            .ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Test Corps", entries[0].Label);
        Assert.Equal(corps.Id, entries[0].CorpsId);
        Assert.Equal("Awards", entries[1].Label);
        Assert.Null(entries[1].CorpsId);

        var savedShow = await db.Shows.FindAsync(show.Id);

        Assert.True(savedShow!.IsExhibition);
        Assert.Equal("Test Venue, City, ST", savedShow.Location);
        Assert.Equal(39.7684, savedShow.Latitude);
        Assert.Null(savedShow.ScoresAnnouncedTime);
    }

    [Fact]
    public async Task ShowScheduleEntryEntity_NullTime_PersistsAsUnscheduled()
    {
        using var db = CreateDb("schedule_entity_null_time");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = 2030,
            StartDate = new DateOnly(2030, 6, 1),
            EndDate = new DateOnly(2030, 8, 31)
        };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Unscheduled Corps" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Show",
            Date = new DateOnly(2030, 7, 4),
            SeasonId = season.Id
        };

        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SortOrder = 0,
            Time = null,
            Label = "Unscheduled Corps",
            CorpsId = corps.Id
        });

        await db.SaveChangesAsync();

        var entry = db.ShowScheduleEntries.Single(e => e.ShowId == show.Id);

        Assert.Null(entry.Time);
        Assert.Equal("Unscheduled Corps", entry.Label);
    }

    [Fact]
    public async Task CreateShowAsync_PersistsScheduleEntries()
    {
        using var db = CreateDb("admin_create_show_with_schedule");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = 2030,
            StartDate = new DateOnly(2030, 6, 1),
            EndDate = new DateOnly(2030, 8, 31)
        };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };

        db.Seasons.Add(season);
        db.Corps.Add(corps);

        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var schedule = new List<ShowScheduleEntryRequest>
        {
            new(new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero), "Blue Devils", corps.Id),
            new(new DateTimeOffset(2030, 7, 5, 0, 0, 0, TimeSpan.Zero), "Awards", null)
        };

        await svc.CreateShowAsync(
            season.Id, "Test Show", null, new DateOnly(2030, 7, 4),
            null, null, "PT", true, "Test Venue", null, null,
            [corps.Id], schedule);

        var entries = db.ShowScheduleEntries
            .OrderBy(e => e.SortOrder)
            .ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Blue Devils", entries[0].Label);
        Assert.Equal(corps.Id, entries[0].CorpsId);
        Assert.Null(entries[1].CorpsId);
    }

    [Fact]
    public async Task CreateShowAsync_NullScheduleTime_PersistsAsUnscheduled()
    {
        using var db = CreateDb("admin_create_show_null_time");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = 2030,
            StartDate = new DateOnly(2030, 6, 1),
            EndDate = new DateOnly(2030, 8, 31)
        };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };

        db.Seasons.Add(season);
        db.Corps.Add(corps);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var schedule = new List<ShowScheduleEntryRequest>
        {
            new(null, "Blue Devils - Concord, CA", corps.Id)
        };

        await svc.CreateShowAsync(
            season.Id, "Test Show", null, new DateOnly(2030, 7, 4),
            null, null, "PT", true, "Test Venue", null, null,
            [corps.Id], schedule);

        var entry = db.ShowScheduleEntries.Single(e => e.CorpsId == corps.Id);

        Assert.Null(entry.Time);
        Assert.Equal("Blue Devils - Concord, CA", entry.Label);
    }

    [Fact]
    public async Task UpdateShowAsync_ReplacesScheduleEntries()
    {
        using var db = CreateDb("admin_update_show_schedule");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = 2030,
            StartDate = new DateOnly(2030, 6, 1),
            EndDate = new DateOnly(2030, 8, 31)
        };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Show",
            Date = new DateOnly(2030, 7, 4),
            SeasonId = season.Id
        };

        db.Seasons.Add(season);
        db.Shows.Add(show);
        db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 0,
            Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
            Label = "Old Entry"
        });

        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
        var newSchedule = new List<ShowScheduleEntryRequest>
        {
            new(new DateTimeOffset(2030, 7, 4, 23, 30, 0, TimeSpan.Zero), "New Entry", null)
        };

        await svc.UpdateShowAsync(
            show.Id, "Test Show", null, new DateOnly(2030, 7, 4),
            null, null, "PT", false, null, null, null, [], newSchedule);

        var entries = db.ShowScheduleEntries.Where(e => e.ShowId == show.Id).ToList();

        Assert.Single(entries);
        Assert.Equal("New Entry", entries[0].Label);
    }

    [Fact]
    public async Task DeleteShowAsync_AlsoDeletesScheduleEntries()
    {
        using var db = CreateDb("admin_delete_show_schedule");

        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2030,
            StartDate = new DateOnly(2030, 6, 1), EndDate = new DateOnly(2030, 8, 31)
        };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show",
            Date = new DateOnly(2030, 7, 4), SeasonId = season.Id
        };

        db.Seasons.Add(season);
        db.Shows.Add(show);
        db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 0,
            Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
            Label = "Entry"
        });

        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        await svc.DeleteShowAsync(show.Id);

        Assert.Empty(db.ShowScheduleEntries.Where(e => e.ShowId == show.Id).ToList());
    }

    [Fact]
    public async Task TriggerScrapeAsync_MissingShow_ReturnsFoundFalse()
    {
        using var db = CreateDb("trigger_scrape_missing");
        var scrapeScheduler = CreateSvc(db, new FakeRecapScraperTask());
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(Guid.NewGuid());

        Assert.False(found);
        Assert.Equal(ScrapeOutcome.Skipped, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task TriggerScrapeAsync_SuccessfulScrape_ReturnsSucceededOutcome()
    {
        using var db = CreateDb("trigger_scrape_success");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scrapeScheduler = CreateSvc(db, new FakeRecapScraperTask(failuresBeforeSuccess: 0));
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(show.Id);

        Assert.True(found);
        Assert.Equal(ScrapeOutcome.Succeeded, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task TriggerScrapeAsync_FailedScrape_ReturnsFailedOutcomeWithError()
    {
        using var db = CreateDb("trigger_scrape_failure");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var scrapeScheduler = CreateSvc(db, scraperTask);
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(show.Id);

        Assert.True(found);
        Assert.Equal(ScrapeOutcome.Failed, outcome);
        Assert.Equal("Simulated scrape failure", error);
        Assert.Equal(1, scraperTask.CallCount);
    }
}
