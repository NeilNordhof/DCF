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

    [Fact]
    public async Task GetStandings_AveragesCorpsScoresPerCaption()
    {
        using var db = CreateDb("standings_avg");

        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, IsActive = true };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var corps3 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "sub|1", Email = "a@b.com", DisplayName = "Alice"
        };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "Test", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user,
            InviteCode = "ABCD1234", CorpsPerCaption = 3,
            DraftableCaptions = [Caption.Brass],
            DraftStatus = DraftStatus.Completed,
            DraftOrderJson = $"[\"{user.Id}\"]"
        };

        db.Seasons.Add(season);
        db.Corps.AddRange(corps1, corps2, corps3);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity
        {
            LeagueId = league.Id, UserId = user.Id, League = league, User = user
        });
        db.DraftPicks.AddRange(
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps1.Id, Caption = Caption.Brass, PickNumber = 0, RoundNumber = 0,
                League = league, User = user, Corps = corps1
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps2.Id, Caption = Caption.Brass, PickNumber = 1, RoundNumber = 0,
                League = league, User = user, Corps = corps2
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps3.Id, Caption = Caption.Brass, PickNumber = 2, RoundNumber = 0,
                League = league, User = user, Corps = corps3
            }
        );
        db.Scores.AddRange(
            new ScoreEntity
            {
                Id = Guid.NewGuid(), CorpsId = corps1.Id, ShowId = show.Id,
                Caption = Caption.Brass, TotalScore = 80.0, Corps = corps1, Show = show
            },
            new ScoreEntity
            {
                Id = Guid.NewGuid(), CorpsId = corps2.Id, ShowId = show.Id,
                Caption = Caption.Brass, TotalScore = 85.0, Corps = corps2, Show = show
            },
            new ScoreEntity
            {
                Id = Guid.NewGuid(), CorpsId = corps3.Id, ShowId = show.Id,
                Caption = Caption.Brass, TotalScore = 90.0, Corps = corps3, Show = show
            }
        );
        await db.SaveChangesAsync();

        var service = new StandingsService(db);

        var standings = await service.GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal((80.0 + 85.0 + 90.0) / 3, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_UsesLatestShowScore()
    {
        using var db = CreateDb("standings_latest");

        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, IsActive = true };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/show1",
            Date = new DateOnly(2025, 7, 1), SeasonId = season.Id, Season = season
        };
        var show2 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show2", Url = "https://dci.org/scores/show2",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "sub|2", Email = "b@c.com", DisplayName = "Bob"
        };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L2", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user,
            InviteCode = "XYZ12345", CorpsPerCaption = 1,
            DraftableCaptions = [Caption.Brass],
            DraftStatus = DraftStatus.Completed,
            DraftOrderJson = $"[\"{user.Id}\"]"
        };

        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity
        {
            LeagueId = league.Id, UserId = user.Id, League = league, User = user
        });
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
            CorpsId = corps.Id, Caption = Caption.Brass, PickNumber = 0, RoundNumber = 0,
            League = league, User = user, Corps = corps
        });
        db.Scores.AddRange(
            new ScoreEntity
            {
                Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show1.Id,
                Caption = Caption.Brass, TotalScore = 70.0, Corps = corps, Show = show1
            },
            new ScoreEntity
            {
                Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show2.Id,
                Caption = Caption.Brass, TotalScore = 88.5, Corps = corps, Show = show2
            }
        );
        await db.SaveChangesAsync();

        var service = new StandingsService(db);

        var standings = await service.GetStandingsAsync(league.Id);

        Assert.Equal(88.5, standings[0].Score, precision: 5);
    }
}
