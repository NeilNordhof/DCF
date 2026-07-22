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
}
