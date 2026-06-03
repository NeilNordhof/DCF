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
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
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
