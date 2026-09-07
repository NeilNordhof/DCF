using DCF.Api.Charts;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Tests.Charts;

internal static class ChartTestHelpers
{
    public static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    public static ChartParameters Params(params (string Key, object? Value)[] values)
    {
        return ChartParameters.FromObjects(values.ToDictionary(v => v.Key, v => v.Value));
    }

    public static SeasonEntity AddSeason(this DcfDbContext db, int year = 2026, bool published = true)
    {
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = year,
            IsPublished = published,
            Status = SeasonStatus.Active,
            StartDate = new DateOnly(year, 6, 1),
            EndDate = new DateOnly(year, 8, 10)
        };
        db.Seasons.Add(season);

        return season;
    }

    public static CorpsEntity AddCorps(this DcfDbContext db, string name)
    {
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = name };
        db.Corps.Add(corps);

        return corps;
    }

    public static ShowEntity AddShow(this DcfDbContext db, SeasonEntity season, string name, DateOnly date)
    {
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Date = date,
            SeasonId = season.Id
        };
        db.Shows.Add(show);

        return show;
    }

    public static void AddTotalScore(this DcfDbContext db, CorpsEntity corps, ShowEntity show, double total)
    {
        db.Scores.Add(new ScoreEntity
        {
            Id = Guid.NewGuid(),
            CorpsId = corps.Id,
            ShowId = show.Id,
            Caption = Caption.Total,
            TotalScore = total,
            TotalRank = 1
        });
    }

    public static UserEntity AddUser(this DcfDbContext db, string sub, string displayName)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = sub,
            Email = $"{sub}@example.com",
            DisplayName = displayName
        };
        db.Users.Add(user);

        return user;
    }

    public static LeagueEntity AddLeague(
        this DcfDbContext db, SeasonEntity season, UserEntity commissioner, ComputedCaption[] captions, string name = "Test League")
    {
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            SeasonId = season.Id,
            CommissionerUserId = commissioner.Id,
            InviteCode = Guid.NewGuid().ToString("N")[..6],
            DraftableCaptions = captions,
            CorpsPerCaption = 1,
            MaxPlayers = 8
        };
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id });

        return league;
    }
}
