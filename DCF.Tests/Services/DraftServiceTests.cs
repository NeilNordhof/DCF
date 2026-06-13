using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace DCF.Tests.Services;

internal sealed class NullMqtt : IMqttService
{
    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class NullPresenceService : IPresenceService
{
    public Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<Guid> GetOnline(Guid leagueId)
    {
        return Array.Empty<Guid>();
    }
}

public class DraftServiceTests
{
    [Fact]
    public void GetCurrentDrafter_Round0Pick0_ReturnsFirstUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 0));
    }

    [Fact]
    public void GetCurrentDrafter_Round0Pick2_ReturnsLastUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 2));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick0_ReturnsLastUser()
    {
        // Round 1 (second round) snakes: C, B, A
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 3));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick1_ReturnsMiddleUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("b", DraftService.GetCurrentDrafter(order, 4));
    }

    [Fact]
    public void GetCurrentDrafter_Round2Pick0_ReturnsFirstUser()
    {
        // Round 2 (third round) snakes forward again: A, B, C
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 6));
    }

    [Fact]
    public void DraftStatus_Open_ExistsBetweenScheduledAndInProgress()
    {
        var values = Enum.GetValues<DraftStatus>().ToList();
        int scheduledIdx = values.IndexOf(DraftStatus.Scheduled);
        int inProgressIdx = values.IndexOf(DraftStatus.InProgress);

        Assert.True(Enum.IsDefined(typeof(DraftStatus), "Open"));
        int openIdx = values.IndexOf(DraftStatus.Open);
        Assert.True(openIdx > scheduledIdx && openIdx < inProgressIdx);
    }
}

public class OpenDraftTests
{
    private sealed class SpyMqtt : IMqttService
    {
        public record Publish(string Topic, bool Retain);
        public List<Publish> Messages { get; } = new();

        public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
        {
            Messages.Add(new(topic, retain));
            return Task.CompletedTask;
        }
    }

    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, SpyMqtt Mqtt, Guid CommissionerId, Guid MemberId, Guid LeagueId) Seed(
        DraftStatus status = DraftStatus.NotStarted)
    {
        var db = CreateDb();
        var mqtt = new SpyMqtt();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var member2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem2", DisplayName = "Member2", Email = "m2@test.com" };
        var member3 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem3", DisplayName = "Member3", Email = "m3@test.com" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = status,
            DraftOrderJson = "[]",
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member, member2, member3);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member2.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member3.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, mqtt, new NullPresenceService()), mqtt, commissioner.Id, member.Id, league.Id);
    }

    [Fact]
    public async Task SchedulerPath_SetsStatusToOpen()
    {
        var (db, svc, _, _, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Open, league!.DraftStatus);
    }

    [Fact]
    public async Task SchedulerPath_PopulatesDraftOrder()
    {
        var (db, svc, _, commId, memId, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.NotEqual("[]", league!.DraftOrderJson);
        Assert.Contains(commId.ToString(), league.DraftOrderJson);
        Assert.Contains(memId.ToString(), league.DraftOrderJson);
    }

    [Fact]
    public async Task SchedulerPath_IsIdempotent_WhenAlreadyOpen()
    {
        var (db, svc, mqtt, _, _, leagueId) = Seed(DraftStatus.Open);
        var league = db.Leagues.Find(leagueId)!;
        league.DraftOrderJson = "[\"existing\"]";
        db.SaveChanges();

        await svc.OpenDraftAsync(leagueId);

        var updated = await db.Leagues.FindAsync(leagueId);
        Assert.Equal("[\"existing\"]", updated!.DraftOrderJson);
        Assert.Empty(mqtt.Messages);
    }

    [Fact]
    public async Task CommissionerPath_SetsStatusToOpen()
    {
        var (db, svc, _, _, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Open, league!.DraftStatus);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotCommissioner()
    {
        var (_, svc, _, _, _, leagueId) = Seed();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.OpenDraftAsync(leagueId, "auth|mem"));
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenStatusIsNotNotStarted()
    {
        var (_, svc, _, _, _, leagueId) = Seed(DraftStatus.Scheduled);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.OpenDraftAsync(leagueId, "auth|comm"));
    }

    [Fact]
    public async Task PublishesRetainedMqttMessage()
    {
        var (_, svc, mqtt, _, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        Assert.Single(mqtt.Messages);
        Assert.True(mqtt.Messages[0].Retain);
    }
}

public class StartDraftTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, Guid CommissionerId, Guid LeagueId) Seed(
        DraftStatus status = DraftStatus.Open)
    {
        var db = CreateDb();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var draftOrder = JsonSerializer.Serialize(new[] { commissioner.Id.ToString(), member.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = status,
            DraftOrderJson = status == DraftStatus.Open ? draftOrder : "[]",
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), commissioner.Id, league.Id);
    }

    [Fact]
    public async Task SchedulerPath_ThrowsWhenNotOpen()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.NotStarted);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartDraftAsync(leagueId));

        Assert.Contains("opened", ex.Message);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotOpen()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.NotStarted);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartDraftAsync(leagueId, "auth|comm"));

        Assert.Contains("opened", ex.Message);
    }

    [Fact]
    public async Task CommissionerPath_SetsStatusToInProgress_WhenOpen()
    {
        var (db, svc, _, leagueId) = Seed(DraftStatus.Open);

        await svc.StartDraftAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotCommissioner()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.Open);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.StartDraftAsync(leagueId, "auth|mem"));
    }
}

public class PublishStateTests
{
    private sealed class CapturingMqtt : IMqttService
    {
        public string? LastPayloadJson { get; private set; }

        public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
        {
            LastPayloadJson = JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return Task.CompletedTask;
        }
    }

    private sealed class FakePresenceService : IPresenceService
    {
        private readonly Guid[] _online;

        public FakePresenceService(params Guid[] online)
        {
            _online = online;
        }

        public Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online)
        {
            return Task.CompletedTask;
        }

        public IReadOnlyCollection<Guid> GetOnline(Guid leagueId)
        {
            return _online;
        }
    }

    private static DcfDbContext CreateDb()
    {
        return new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    [Fact]
    public async Task PublishStateAsync_LeagueNotFound_DoesNotThrow()
    {
        var db = CreateDb();
        var svc = new DraftService(db, new NullMqtt(), new NullPresenceService());

        var ex = await Record.ExceptionAsync(() => svc.PublishStateAsync(Guid.NewGuid()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishStateAsync_IncludesOnlineUserIds()
    {
        var db = CreateDb();
        var mqtt = new CapturingMqtt();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var presence = new FakePresenceService(user1, user2);
        var svc = new DraftService(db, mqtt, presence);

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CommissionerUserId = user1,
            DraftStatus = DraftStatus.Open,
            DraftOrderJson = "[]",
            InviteCode = "ABCD1234",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Leagues.Add(league);

        await db.SaveChangesAsync();

        await svc.PublishStateAsync(league.Id);

        Assert.NotNull(mqtt.LastPayloadJson);

        using var doc = JsonDocument.Parse(mqtt.LastPayloadJson!);
        var ids = doc.RootElement.GetProperty("onlineUserIds")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet();

        Assert.Contains(user1.ToString(), ids);
        Assert.Contains(user2.ToString(), ids);
    }

    [Fact]
    public async Task PublishStateAsync_WithSkip_IncludesMakeupQueueAndMainTotalPicks()
    {
        var db = CreateDb();
        var mqtt = new CapturingMqtt();
        var user1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "a1", DisplayName = "U1", Email = "u1@t.com" };
        var user2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "a2", DisplayName = "U2", Email = "u2@t.com" };
        var svc = new DraftService(db, mqtt, new NullPresenceService());
        var draftOrder = JsonSerializer.Serialize(new[] { user1.Id.ToString(), user2.Id.ToString() });

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "T", CommissionerUserId = user1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder, CurrentPickNumber = 1,
            InviteCode = "ABCD",
            DraftableCaptions = [ComputedCaption.Brass], CorpsPerCaption = 1
        };

        db.Users.AddRange(user1, user2);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = user1.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = user2.Id }
        );
        // No DraftPick at slot 0 — user1 was skipped; slot 1 is the current pick (not yet made)
        await db.SaveChangesAsync();

        await svc.PublishStateAsync(league.Id);

        Assert.NotNull(mqtt.LastPayloadJson);
        using var doc = JsonDocument.Parse(mqtt.LastPayloadJson!);
        var mainTotalPicks = doc.RootElement.GetProperty("mainTotalPicks").GetInt32();
        var makeupQueue = doc.RootElement.GetProperty("makeupQueue")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Equal(2, mainTotalPicks);
        Assert.Single(makeupQueue);
        Assert.Equal(user1.Id.ToString(), makeupQueue[0]);
    }
}

public class SubmitPickTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, Guid PlayerId, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) Seed()
    {
        var db = CreateDb();
        var player = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|player", DisplayName = "Player", Email = "p@test.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = player.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = 0,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.Add(player);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = player.Id });
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), player.Id, league.Id, corps1.Id, corps2.Id);
    }

    [Fact]
    public async Task SubmitPick_ThrowsWhenCaptionQuotaExceeded()
    {
        var (db, svc, playerId, leagueId, corps1Id, corps2Id) = Seed();

        // Pre-seed a pick for corps1+Brass, filling the quota of 1
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = playerId,
            CorpsId = corps1Id, Caption = ComputedCaption.Brass,
            PickNumber = 0, RoundNumber = 0
        });
        await db.SaveChangesAsync();

        // Attempt to pick corps2+Brass (not taken, but quota already met)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitPickAsync(leagueId, "auth|player", corps2Id, ComputedCaption.Brass));

        Assert.Contains("maximum", ex.Message);
    }

    [Fact]
    public async Task SubmitPick_SucceedsWhenWithinQuota()
    {
        var (db, svc, _, leagueId, corps1Id, _) = Seed();

        var (id, pickNumber) = await svc.SubmitPickAsync(leagueId, "auth|player", corps1Id, ComputedCaption.Brass);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(0, pickNumber);
    }
}

public class SkipCurrentPickTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, Guid CommissionerId, Guid MemberId, Guid LeagueId) Seed(
        int currentPickNumber = 0)
    {
        var db = CreateDb();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var draftOrder = JsonSerializer.Serialize(new[] { commissioner.Id.ToString(), member.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = currentPickNumber,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), commissioner.Id, member.Id, league.Id);
    }

    [Fact]
    public async Task Skip_LastMainPick_DraftStaysInProgress()
    {
        // mainTotalPicks = 2 (2 players × 1 caption × 1 corps per caption)
        // CurrentPickNumber = 1 means we are on the last main-draft slot
        var (db, svc, _, _, leagueId) = Seed(currentPickNumber: 1);

        await svc.SkipCurrentPickAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
        Assert.Equal(2, league.CurrentPickNumber);
    }

    [Fact]
    public async Task Skip_DuringMakeupPhase_Throws()
    {
        // CurrentPickNumber = 2 = mainTotalPicks → already in makeup phase
        var (_, svc, _, _, leagueId) = Seed(currentPickNumber: 2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SkipCurrentPickAsync(leagueId, "auth|comm"));

        Assert.Contains("makeup", ex.Message);
    }
}

public class SubmitPickMakeupTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Seeds a league where slot 0 was skipped (Player1) and slot 1 was picked by Player2.
    // CurrentPickNumber = 2 = mainTotalPicks → draft is in makeup phase.
    // Player1 still needs a makeup pick; corps1 is taken; corps2 is free.
    private static (DcfDbContext Db, DraftService Service, Guid Player1Id, Guid Player2Id, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) SeedMakeupPhase()
    {
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "Player1", Email = "p1@test.com" };
        var player2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p2", DisplayName = "Player2", Email = "p2@test.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString(), player2.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = 2,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(player1, player2);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player2.Id }
        );
        // Slot 0 has no pick (Player1 was skipped). Slot 1 was picked by Player2.
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = player2.Id,
            CorpsId = corps1.Id, Caption = ComputedCaption.Brass,
            PickNumber = 1, RoundNumber = 0
        });
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()),
            player1.Id, player2.Id, league.Id, corps1.Id, corps2.Id);
    }

    // Seeds a league where slot 0 was skipped (Player1) and the main draft is still
    // in progress at slot 1 (Player2's turn). CurrentPickNumber = 1.
    private static (DcfDbContext Db, DraftService Service, Guid Player1Id, Guid Player2Id, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) SeedLastMainPickPending()
    {
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "Player1", Email = "p1@test.com" };
        var player2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p2", DisplayName = "Player2", Email = "p2@test.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString(), player2.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = 1,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(player1, player2);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player2.Id }
        );
        // No DraftPick at slot 0 — Player1 was skipped. Slot 1 is pending (Player2's turn).
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()),
            player1.Id, player2.Id, league.Id, corps1.Id, corps2.Id);
    }

    [Fact]
    public async Task LastMainPick_WithPendingSkip_DraftStaysInProgress()
    {
        var (db, svc, _, _, leagueId, corps1Id, _) = SeedLastMainPickPending();

        await svc.SubmitPickAsync(leagueId, "auth|p2", corps1Id, ComputedCaption.Brass);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
    }

    [Fact]
    public async Task MakeupPick_CreatesPickAtGapSlot_AndCompletesDraft()
    {
        var (db, svc, _, _, leagueId, _, corps2Id) = SeedMakeupPhase();

        var (id, pickNumber) = await svc.SubmitPickAsync(leagueId, "auth|p1", corps2Id, ComputedCaption.Brass);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(0, pickNumber); // gap was at slot 0

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Completed, league!.DraftStatus);
    }

    [Fact]
    public async Task MakeupPick_NonSkippedUser_Throws()
    {
        var (_, svc, _, _, leagueId, _, corps2Id) = SeedMakeupPhase();
        // Player2 was not skipped — they have no makeup picks

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitPickAsync(leagueId, "auth|p2", corps2Id, ComputedCaption.Brass));

        Assert.Contains("no makeup picks", ex.Message);
    }

    [Fact]
    public async Task MakeupPick_UserSkippedTwice_FirstPickFillsEarliestGap_DraftStaysInProgress()
    {
        // 1 player, 2 captions, 1 corps per caption → mainTotalPicks = 2
        // Skip both slots (slot 0 and slot 1) → 2 makeup picks for Player1
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "P1", Email = "p1@t.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "T", CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder, CurrentPickNumber = 2,
            InviteCode = "T", DraftableCaptions = [ComputedCaption.Brass, ComputedCaption.Percussion],
            CorpsPerCaption = 1
        };
        db.Users.Add(player1);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id });
        // Slots 0 and 1 both skipped — no DraftPicks
        db.SaveChanges();
        var svc = new DraftService(db, new NullMqtt(), new NullPresenceService());

        await svc.SubmitPickAsync(league.Id, "auth|p1", corps1.Id, ComputedCaption.Brass);

        var updatedLeague = await db.Leagues.FindAsync(league.Id);
        Assert.Equal(DraftStatus.InProgress, updatedLeague!.DraftStatus); // one gap still remains

        var pick = await db.DraftPicks.SingleAsync(p => p.LeagueId == league.Id);
        Assert.Equal(0, pick.PickNumber); // filled earliest gap
    }
}
