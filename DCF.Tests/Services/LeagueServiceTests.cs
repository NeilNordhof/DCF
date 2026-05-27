using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
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

    private static async Task<(SeasonEntity Season, UserEntity User)> CreateSeasonAndUser(
        DcfDbContext db, int corpsCount, string userSub)
    {
        var season = new SeasonEntity { Year = 2026, IsPublished = true };
        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        for (var i = 0; i < corpsCount; i++)
        {
            var corps = new CorpsEntity { Name = $"Corps {i}" };
            db.Corps.Add(corps);

            await db.SaveChangesAsync();

            db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps.Id });
        }

        var user = new UserEntity { Auth0Sub = userSub, DisplayName = userSub, Email = $"{userSub}@test.com" };
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return (season, user);
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

    // ── CreateAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidParams_SetsMaxPlayers()
    {
        await using var db = CreateDb(nameof(CreateAsync_ValidParams_SetsMaxPlayers));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 24, userSub: "sub|me");

        var svc = new LeagueService(db, null!);
        var league = await svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
            maxPlayers: 8, captions: [ComputedCaption.MusicCombined], userSub: "sub|me");

        Assert.Equal(8, league.MaxPlayers);
    }

    [Fact]
    public async Task CreateAsync_MaxPlayersBelowMinimum_Throws()
    {
        await using var db = CreateDb(nameof(CreateAsync_MaxPlayersBelowMinimum_Throws));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 24, userSub: "sub|me");

        var svc = new LeagueService(db, null!);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
                maxPlayers: 2, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
    }

    [Fact]
    public async Task CreateAsync_CorpsPerCaptionTooHigh_Throws()
    {
        await using var db = CreateDb(nameof(CreateAsync_CorpsPerCaptionTooHigh_Throws));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 24, userSub: "sub|me");

        var svc = new LeagueService(db, null!);
        // floor(24/4) = 6, so 7 is invalid
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 7,
                maxPlayers: 4, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
    }

    [Fact]
    public async Task CreateAsync_MaxPlayersExceedsFloor_Throws()
    {
        await using var db = CreateDb(nameof(CreateAsync_MaxPlayersExceedsFloor_Throws));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 12, userSub: "sub|me");

        var svc = new LeagueService(db, null!);
        // 12 corps, corpsPerCaption=3 → floor(12/3) = 4 max
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
                maxPlayers: 5, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
    }
}
