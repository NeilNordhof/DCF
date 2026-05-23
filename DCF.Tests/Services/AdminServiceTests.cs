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
        public void ScheduleSeason(SeasonEntity season) { }
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
}
