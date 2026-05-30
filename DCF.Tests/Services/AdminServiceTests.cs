using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

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
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var result = await svc.RenameCorpsAsync(corps.Id, "New Name");

        Assert.NotNull(result);
        Assert.Equal("New Name", result!.Name);
        Assert.Equal("New Name", db.Corps.Single(c => c.Id == corps.Id).Name);
    }

    [Fact]
    public async Task RenameCorps_MissingId_ReturnsNull()
    {
        using var db = CreateDb("corps_rename_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var (found, oldPath) = await svc.SetCorpsIconAsync(corps.Id, "corps-icons/cav.png");

        Assert.True(found);
        Assert.Null(oldPath);
        Assert.Equal("corps-icons/cav.png", db.Corps.Single(c => c.Id == corps.Id).IconPath);
    }

    [Fact]
    public async Task SetCorpsIconAsync_MissingCorps_ReturnsFalse()
    {
        using var db = CreateDb("corps_icon_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var result = await svc.UpdateSeasonDatesAsync(season.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 1));

        Assert.False(result);
        Assert.Equal(new DateOnly(2026, 6, 1), db.Seasons.Single(s => s.Id == season.Id).StartDate);
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

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var result = await svc.DeleteShowAsync(show.Id);

        Assert.True(result);
        Assert.False(db.Shows.Any(s => s.Id == show.Id));
    }
}
