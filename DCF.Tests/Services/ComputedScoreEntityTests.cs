using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class ComputedScoreEntityTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task ComputedScoreEntity_CanBeAddedAndRetrieved()
    {
        using var db = CreateDb("computed_score_basic");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025 };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/show1",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = season.Id,
            CorpsId = corps.Id,
            GeneralEffectCombined = 38.5,
            GeneralEffect1 = 19.25,
            GeneralEffect2 = 19.25,
            VisualCombined = 28.5,
            Visual = 19.0,
            Colorguard = 18.0,
            VisualProficiency = 18.5,
            VisualAnalysis = 19.5,
            MusicCombined = 29.0,
            Brass = 19.0,
            Percussion = 18.5,
            MusicAnalysis = 19.0
        });
        await db.SaveChangesAsync();

        var loaded = await db.ComputedScores
            .FirstAsync(cs => cs.ShowId == show.Id && cs.CorpsId == corps.Id);

        Assert.Equal(38.5, loaded.GeneralEffectCombined, precision: 5);
        Assert.Equal(29.0, loaded.MusicCombined, precision: 5);
        Assert.Equal(season.Id, loaded.SeasonId);
    }
}
