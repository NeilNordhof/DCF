using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DCF.Tests.Services;

internal sealed class NullEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        return Task.CompletedTask;
    }
}

public class LeagueServiceTests
{
    private sealed class NoOpStandings : IStandingsService
    {
        public Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
        {
            return Task.FromResult(new List<MemberStanding>());
        }

        public Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
        {
            return Task.FromResult(new List<MemberScoreBreakdown>());
        }

        public Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId)
        {
            return Task.FromResult<(int?, double?)>((null, null));
        }
    }

    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static LeagueService CreateSvc(DcfDbContext db)
    {
        var emailOpts = Options.Create(new EmailOptions { UnsubscribeSecret = "test-secret" });
        var tokenSvc = new EmailTokenService(emailOpts);

        return new LeagueService(
            db,
            null!,
            new NoOpStandings(),
            new NullEmailService(),
            emailOpts,
            tokenSvc,
            NullLogger<LeagueService>.Instance);
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

        var svc = CreateSvc(db);
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

        var svc = CreateSvc(db);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode));
        var (_, league) = await CreateSeasonAndLeague(db, "Private", isPublic: false, inviteCode: "ABC123");

        var svc = CreateSvc(db);
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

        var svc = CreateSvc(db);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: "WRONG");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PublicLeague_NullUserSub_ReturnsLeague()
    {
        await using var db = CreateDb(nameof(GetAsync_PublicLeague_NullUserSub_ReturnsLeague));
        var (_, league) = await CreateSeasonAndLeague(db, "Open", isPublic: true);

        var svc = CreateSvc(db);
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

        var svc = CreateSvc(db);
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

        var svc = CreateSvc(db);
        var league = await svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
            maxPlayers: 8, captions: [ComputedCaption.MusicCombined], userSub: "sub|me");

        Assert.Equal(8, league.MaxPlayers);
    }

    [Fact]
    public async Task CreateAsync_MaxPlayersBelowMinimum_Throws()
    {
        await using var db = CreateDb(nameof(CreateAsync_MaxPlayersBelowMinimum_Throws));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 24, userSub: "sub|me");

        var svc = CreateSvc(db);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
                maxPlayers: 2, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
    }

    [Fact]
    public async Task CreateAsync_CorpsPerCaptionTooHigh_Throws()
    {
        await using var db = CreateDb(nameof(CreateAsync_CorpsPerCaptionTooHigh_Throws));
        var (season, user) = await CreateSeasonAndUser(db, corpsCount: 24, userSub: "sub|me");

        var svc = CreateSvc(db);
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

        var svc = CreateSvc(db);
        // 12 corps, corpsPerCaption=3 → floor(12/3) = 4 max
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
                maxPlayers: 5, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
    }

    // ── JoinAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinAsync_LeagueFull_ReturnsFull()
    {
        await using var db = CreateDb(nameof(JoinAsync_LeagueFull_ReturnsFull));
        var owner = new UserEntity { Auth0Sub = "sub|owner", DisplayName = "Owner", Email = "o@test.com" };
        var member = new UserEntity { Auth0Sub = "sub|member", DisplayName = "Member", Email = "m@test.com" };
        var joiner = new UserEntity { Auth0Sub = "sub|joiner", DisplayName = "Joiner", Email = "j@test.com" };
        var league = new LeagueEntity { Name = "Full League", IsPublic = true, MaxPlayers = 2, InviteCode = "X" };
        db.Users.AddRange(owner, member, joiner);
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = owner.Id });
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id });

        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        var result = await svc.JoinAsync(league.Id, "sub|joiner", inviteCode: null);

        Assert.Equal(JoinResult.Full, result);
    }

    [Fact]
    public async Task JoinAsync_LeagueNotFull_ReturnsSuccess()
    {
        await using var db = CreateDb(nameof(JoinAsync_LeagueNotFull_ReturnsSuccess));
        var owner = new UserEntity { Auth0Sub = "sub|owner", DisplayName = "Owner", Email = "o@test.com" };
        var joiner = new UserEntity { Auth0Sub = "sub|joiner", DisplayName = "Joiner", Email = "j@test.com" };
        var league = new LeagueEntity { Name = "Open League", IsPublic = true, MaxPlayers = 8, InviteCode = "X" };
        db.Users.AddRange(owner, joiner);
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = owner.Id });

        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        var result = await svc.JoinAsync(league.Id, "sub|joiner", inviteCode: null);

        Assert.Equal(JoinResult.Ok, result);
    }

    // ── BrowseAsync (my leagues) ─────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_ReturnsOnlyUserLeagues()
    {
        await using var db = CreateDb(nameof(BrowseAsync_ReturnsOnlyUserLeagues));
        var me = new UserEntity { Auth0Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
        var other = new UserEntity { Auth0Sub = "sub|other", DisplayName = "Other", Email = "other@test.com" };
        var season = new SeasonEntity { Year = 2026 };
        db.Users.AddRange(me, other);
        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        var myLeague = new LeagueEntity { Name = "Mine", IsPublic = false, InviteCode = "A", MaxPlayers = 8, SeasonId = season.Id };
        var otherLeague = new LeagueEntity { Name = "Theirs", IsPublic = true, InviteCode = "B", MaxPlayers = 8, SeasonId = season.Id };
        db.Leagues.AddRange(myLeague, otherLeague);

        await db.SaveChangesAsync();

        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = myLeague.Id, UserId = me.Id });
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = otherLeague.Id, UserId = other.Id });

        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        var result = await svc.BrowseAsync("sub|me");

        Assert.Single(result);
        Assert.Equal("Mine", result[0].Name);
    }

    [Fact]
    public async Task GetUserRankAsync_EmptyLeague_ReturnsNullNull()
    {
        await using var db = CreateDb(nameof(GetUserRankAsync_EmptyLeague_ReturnsNullNull));
        var season = new SeasonEntity { Year = 2026 };
        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        var league = new LeagueEntity { Name = "L", MaxPlayers = 8, InviteCode = "X", SeasonId = season.Id };
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        var svc = new StandingsService(db);
        var (rank, score) = await svc.GetUserRankAsync(league.Id, Guid.NewGuid());

        Assert.Null(rank);
        Assert.Null(score);
    }

    // ── GetPublicLeaguesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicLeaguesAsync_ReturnsOnlyPublicLeagues()
    {
        await using var db = CreateDb(nameof(GetPublicLeaguesAsync_ReturnsOnlyPublicLeagues));
        var (_, publicLeague) = await CreateSeasonAndLeague(db, "Public League", isPublic: true);
        var (_, privateLeague) = await CreateSeasonAndLeague(db, "Private League", isPublic: false, inviteCode: "SECRET");

        var svc = CreateSvc(db);
        var result = await svc.GetPublicLeaguesAsync();

        Assert.Single(result);
        Assert.Equal("Public League", result[0].Name);
    }

    [Fact]
    public async Task GetPublicLeaguesAsync_IncludesMemberCount()
    {
        await using var db = CreateDb(nameof(GetPublicLeaguesAsync_IncludesMemberCount));
        var user = new UserEntity { Auth0Sub = "sub|member", DisplayName = "Member", Email = "member@test.com" };
        db.Users.Add(user);

        await db.SaveChangesAsync();

        var (_, league) = await CreateSeasonAndLeague(db, "Public League", isPublic: true, maxPlayers: 8);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });

        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        var result = await svc.GetPublicLeaguesAsync();

        Assert.Equal(1, result[0].MemberCount);
        Assert.Equal(8, result[0].MaxPlayers);
    }

    // ── LookupByCodeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupByCodeAsync_ValidCode_ReturnsLeagueId()
    {
        await using var db = CreateDb(nameof(LookupByCodeAsync_ValidCode_ReturnsLeagueId));
        var season = new SeasonEntity { Year = 2025, IsPublished = true };
        db.Seasons.Add(season);
        var league = new LeagueEntity { Name = "L", InviteCode = "MYCODE", MaxPlayers = 8, SeasonId = season.Id };
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        var result = await svc.LookupByCodeAsync("MYCODE");

        Assert.Equal(league.Id, result);
    }

    [Fact]
    public async Task LookupByCodeAsync_InvalidCode_ReturnsNull()
    {
        await using var db = CreateDb(nameof(LookupByCodeAsync_InvalidCode_ReturnsNull));
        var svc = CreateSvc(db);
        var result = await svc.LookupByCodeAsync("NOPE");

        Assert.Null(result);
    }
}
