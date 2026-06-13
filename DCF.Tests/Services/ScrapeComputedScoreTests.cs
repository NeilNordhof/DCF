using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class ScrapeComputedScoreTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task ComputeAndUpsert_CreatesRowWithCorrectValues()
    {
        using var db = CreateDb("scrape_compute_create");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/test",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);

        // One GE Visual judge; two GE Music judges → averaged
        // VP, VA, CG single; Brass single
        // Two Percussion judges (Single takes first); one Music Analysis judge → averaged
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectMusic, Judge = "A", TotalScore = 19.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectMusic, Judge = "B", TotalScore = 18.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectVisual, Judge = "C", TotalScore = 17.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.VisualProficiency, TotalScore = 18.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.VisualAnalysis, TotalScore = 19.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.ColorGuard, TotalScore = 17.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Brass, TotalScore = 19.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Percussion, Judge = "D", TotalScore = 18.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Percussion, Judge = "E", TotalScore = 17.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.MusicAnalysis, TotalScore = 18.0, Corps = corps, Show = show }
        );
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

        var computed = await db.ComputedScores
            .FirstAsync(cs => cs.ShowId == show.Id && cs.CorpsId == corps.Id);

        double ge1 = 17.5;                  // GeneralEffectVisual → GeneralEffect1
        double ge2 = (19.0 + 18.0) / 2;    // GeneralEffectMusic avg → GeneralEffect2
        double vp = 18.0;
        double va = 19.0;
        double cg = 17.0;
        double brass = 19.5;
        double perc = 18.5;                 // Single → first score (Judge D)
        double ma = 18.0;

        Assert.Equal(ge1, computed.GeneralEffect1, precision: 5);
        Assert.Equal(ge2, computed.GeneralEffect2, precision: 5);
        Assert.Equal(ge1 + ge2, computed.GeneralEffectCombined, precision: 5);
        Assert.Equal((vp + va) / 2, computed.Visual, precision: 5);
        Assert.Equal(cg, computed.Colorguard, precision: 5);
        Assert.Equal((vp + va + cg) / 2, computed.VisualCombined, precision: 5);
        Assert.Equal(vp, computed.VisualProficiency, precision: 5);
        Assert.Equal(va, computed.VisualAnalysis, precision: 5);
        Assert.Equal(brass, computed.Brass, precision: 5);
        Assert.Equal(perc, computed.Percussion, precision: 5);
        Assert.Equal(ma, computed.MusicAnalysis, precision: 5);
        Assert.Equal((brass + ma + perc) / 2, computed.MusicCombined, precision: 5);
        Assert.Equal(season.Id, computed.SeasonId);
    }

    [Fact]
    public async Task ComputeAndUpsert_UpdatesExistingRowForSameShow()
    {
        using var db = CreateDb("scrape_compute_update");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show2", Url = "https://dci.org/scores/test2",
            Date = new DateOnly(2025, 8, 1), SeasonId = season.Id, Season = season
        };
        var existingComputed = new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, Brass = 10.0
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.ComputedScores.Add(existingComputed);
        db.Scores.Add(new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
            Caption = Caption.Brass, TotalScore = 19.0, Corps = corps, Show = show
        });
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

        var allRows = await db.ComputedScores
            .Where(cs => cs.SeasonId == season.Id && cs.CorpsId == corps.Id)
            .ToListAsync();

        Assert.Single(allRows);
        Assert.Equal(19.0, allRows[0].Brass, precision: 5);
    }

    [Fact]
    public async Task ComputeAndUpsert_CreatesNewRowForDifferentShow_PreservingHistory()
    {
        using var db = CreateDb("scrape_compute_history");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/s1",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        var show2 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show2", Url = "https://dci.org/scores/s2",
            Date = new DateOnly(2025, 8, 1), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show1.Id, SeasonId = season.Id,
            CorpsId = corps.Id, Brass = 17.0
        });
        db.Scores.Add(new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show2.Id,
            Caption = Caption.Brass, TotalScore = 19.5, Corps = corps, Show = show2
        });
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show2.Id, season.Id);

        var rows = await db.ComputedScores
            .Where(cs => cs.SeasonId == season.Id && cs.CorpsId == corps.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ShowId == show1.Id && r.Brass == 17.0);
        Assert.Contains(rows, r => r.ShowId == show2.Id && r.Brass == 19.5);
    }
}
