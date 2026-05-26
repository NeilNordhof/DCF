using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class StandingsServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    private static async Task<(SeasonEntity Season, CorpsEntity Corps, ShowEntity Show, UserEntity User, LeagueEntity League)>
        SeedLeagueAsync(DcfDbContext db, ComputedCaption[] captions, string inviteCode, int corpsPerCaption = 1)
    {
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|s1", Email = "a@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = inviteCode,
            CorpsPerCaption = corpsPerCaption, DraftableCaptions = captions,
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        await db.SaveChangesAsync();
        return (season, corps, show, user, league);
    }

    private static DraftPickEntity Pick(LeagueEntity league, UserEntity user, CorpsEntity corps,
        ComputedCaption caption, int pickNum)
    {
        return new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
            CorpsId = corps.Id, Caption = caption, PickNumber = pickNum, RoundNumber = 0,
            League = league, User = user, Corps = corps
        };
    }

    private static ComputedScoreEntity ComputedScore(ShowEntity show, CorpsEntity corps,
        double ge1 = 0, double ge2 = 0, double vp = 0, double va = 0, double cg = 0,
        double brass = 0, double perc = 0, double ma = 0)
    {
        return new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = show.SeasonId,
            CorpsId = corps.Id,
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
        };
    }

    [Fact]
    public async Task GetStandings_GECombined_FullWeight()
    {
        using var db = CreateDb("standings_ge_combined");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.GeneralEffectCombined], "GEC12345");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.GeneralEffectCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, GeneralEffectCombined = 38.5
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal(38.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_VisualCombined_FullWeight()
    {
        using var db = CreateDb("standings_visual_combined");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.VisualCombined], "VIS12345");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.VisualCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, VisualCombined = 28.5
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Equal(28.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Visual2Split_75PercentWeight()
    {
        using var db = CreateDb("standings_vis2");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Visual, ComputedCaption.Colorguard], "V2S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Visual, 0),
            Pick(league, user, corps, ComputedCaption.Colorguard, 1)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, va: 19.0, vp: 19.0, cg: 17.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // Visual = avg(19.0, 19.0) = 19.0 → 19.0 * 0.75 = 14.25
        // Colorguard = 17.0 → 17.0 * 0.75 = 12.75
        Assert.Equal(27.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Visual3Split_50PercentWeight()
    {
        using var db = CreateDb("standings_vis3");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.VisualProficiency, ComputedCaption.VisualAnalysis, ComputedCaption.Colorguard],
            "V3S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.VisualProficiency, 0),
            Pick(league, user, corps, ComputedCaption.VisualAnalysis, 1),
            Pick(league, user, corps, ComputedCaption.Colorguard, 2)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, vp: 18.0, va: 19.0, cg: 17.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 18.0 * 0.5 + 19.0 * 0.5 + 17.0 * 0.5 = 9.0 + 9.5 + 8.5 = 27.0
        Assert.Equal(27.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Music2Split_75PercentWeight()
    {
        using var db = CreateDb("standings_mus2");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass, ComputedCaption.Percussion], "M2S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Brass, 0),
            Pick(league, user, corps, ComputedCaption.Percussion, 1)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, brass: 19.5, perc: 18.5));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 19.5 * 0.75 + 18.5 * 0.75 = 14.625 + 13.875 = 28.5
        Assert.Equal(28.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Music3Split_50PercentWeight()
    {
        using var db = CreateDb("standings_mus3");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass, ComputedCaption.Percussion, ComputedCaption.MusicAnalysis],
            "M3S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Brass, 0),
            Pick(league, user, corps, ComputedCaption.Percussion, 1),
            Pick(league, user, corps, ComputedCaption.MusicAnalysis, 2)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, brass: 19.5, perc: 18.5, ma: 18.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 19.5 * 0.5 + 18.5 * 0.5 + 18.0 * 0.5 = 9.75 + 9.25 + 9.0 = 28.0
        Assert.Equal(28.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_MultipleCorps_AveragesBeforeWeighting()
    {
        using var db = CreateDb("standings_multi_corps");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|mc", Email = "mc@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = "MULTI123",
            CorpsPerCaption = 2, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.AddRange(corps1, corps2);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.AddRange(
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps1.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
                League = league, User = user, Corps = corps1
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps2.Id, Caption = ComputedCaption.Brass, PickNumber = 1, RoundNumber = 0,
                League = league, User = user, Corps = corps2
            }
        );
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corps1.Id, Brass = 20.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corps2.Id, Brass = 16.0 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // avg(20.0, 16.0) = 18.0 → 2-split weight 0.75 → 13.5
        Assert.Equal(13.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_UsesLatestShowScorePerCorps()
    {
        using var db = CreateDb("standings_latest_show");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Prelims", Url = "https://dci.org/scores/prelims",
            Date = new DateOnly(2025, 8, 9), SeasonId = season.Id, Season = season
        };
        var show2 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|ls", Email = "ls@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = "LATEST12",
            CorpsPerCaption = 1, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
            CorpsId = corps.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
            League = league, User = user, Corps = corps
        });
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show1.Id, SeasonId = season.Id, CorpsId = corps.Id, Brass = 17.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show2.Id, SeasonId = season.Id, CorpsId = corps.Id, Brass = 19.5 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // Should use show2 (later date): 19.5 * 0.75 = 14.625
        Assert.Equal(14.625, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_ZeroScore_WhenNoComputedScoreRow()
    {
        using var db = CreateDb("standings_no_computed");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass], "NOCOMP12");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.Brass, 0));
        // No ComputedScoreEntity added
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal(0.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_PopulatesCaptionsDictionary()
    {
        using var db = CreateDb("standings_captions_dict");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.GeneralEffectCombined], "CAPTDICT1");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.GeneralEffectCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, GeneralEffectCombined = 38.0
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.True(standings[0].Captions.ContainsKey(ComputedCaption.GeneralEffectCombined));
        Assert.Equal(38.0, standings[0].Captions[ComputedCaption.GeneralEffectCombined].Avg, precision: 5);
        Assert.Single(standings[0].Captions[ComputedCaption.GeneralEffectCombined].Picks);
        Assert.Equal("Blue Devils",
            standings[0].Captions[ComputedCaption.GeneralEffectCombined].Picks[0].CorpsName);
    }

    [Fact]
    public async Task GetStandings_OrderedByScoreDescending()
    {
        using var db = CreateDb("standings_ordering");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corpsA = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corpsB = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var userA = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|a", Email = "a@b.com", DisplayName = "Alice" };
        var userB = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|b", Email = "b@b.com", DisplayName = "Bob" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = userA.Id, Commissioner = userA, InviteCode = "ORDR1234",
            CorpsPerCaption = 1, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{userA.Id}\",\"{userB.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.AddRange(corpsA, corpsB);
        db.Shows.Add(show);
        db.Users.AddRange(userA, userB);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = userA.Id, League = league, User = userA },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = userB.Id, League = league, User = userB }
        );
        db.DraftPicks.AddRange(
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = userA.Id,
                CorpsId = corpsA.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
                League = league, User = userA, Corps = corpsA
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = userB.Id,
                CorpsId = corpsB.Id, Caption = ComputedCaption.Brass, PickNumber = 1, RoundNumber = 0,
                League = league, User = userB, Corps = corpsB
            }
        );
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corpsA.Id, Brass = 15.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corpsB.Id, Brass = 20.0 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Equal("Bob", standings[0].DisplayName);   // 20.0 * 0.75 = 15.0
        Assert.Equal("Alice", standings[1].DisplayName); // 15.0 * 0.75 = 11.25
    }
}
