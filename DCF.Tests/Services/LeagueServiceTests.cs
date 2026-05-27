using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class LeagueServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static async Task<(SeasonEntity Season, LeagueEntity League)> CreateSeasonAndLeague(
        DcfDbContext db, string leagueName, bool isPublic, string? inviteCode = null, int maxPlayers = 8)
    {
        var season = new SeasonEntity { Year = 2026 };
        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        var league = new LeagueEntity
        {
            Name = leagueName,
            IsPublic = isPublic,
            InviteCode = inviteCode ?? string.Empty,
            MaxPlayers = maxPlayers,
            SeasonId = season.Id
        };
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        return (season, league);
    }

    // ── GetAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_PublicLeague_NonMemberNoCode_ReturnsLeague()
    {
        await using var db = CreateDb(nameof(GetAsync_PublicLeague_NonMemberNoCode_ReturnsLeague));
        var (_, league) = await CreateSeasonAndLeague(db, "Open", isPublic: true);

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: null);

        Assert.NotNull(result);
        Assert.False(result!.IsMember);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberNoCode_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberNoCode_ReturnsNull));
        var (_, league) = await CreateSeasonAndLeague(db, "Private", isPublic: false, inviteCode: "ABC123");

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode));
        var (_, league) = await CreateSeasonAndLeague(db, "Private", isPublic: false, inviteCode: "ABC123");

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: "ABC123");

        Assert.NotNull(result);
        Assert.False(result!.IsMember);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberWrongCode_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberWrongCode_ReturnsNull));
        var (_, league) = await CreateSeasonAndLeague(db, "Private", isPublic: false, inviteCode: "ABC123");

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: "WRONG");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PublicLeague_NullUserSub_ReturnsLeague()
    {
        await using var db = CreateDb(nameof(GetAsync_PublicLeague_NullUserSub_ReturnsLeague));
        var (_, league) = await CreateSeasonAndLeague(db, "Open", isPublic: true);

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: null, inviteCode: null);

        Assert.NotNull(result);
        Assert.False(result!.IsMember);
    }

    [Fact]
    public async Task GetAsync_Member_ReturnsLeagueWithInviteCode()
    {
        await using var db = CreateDb(nameof(GetAsync_Member_ReturnsLeagueWithInviteCode));
        var user = new UserEntity { Auth0Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
        db.Users.Add(user);

        var (_, league) = await CreateSeasonAndLeague(db, "Mine", isPublic: false, inviteCode: "SECRET");

        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });

        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|me", inviteCode: null);

        Assert.NotNull(result);
        Assert.True(result!.IsMember);
        Assert.Equal("SECRET", result.InviteCode);
    }
}
