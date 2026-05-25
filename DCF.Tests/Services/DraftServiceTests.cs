using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace DCF.Tests.Services;

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
            DraftableCaptions = [Caption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, mqtt), mqtt, commissioner.Id, member.Id, league.Id);
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
    private sealed class NullMqtt : IMqttService
    {
        public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

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
            DraftableCaptions = [Caption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt()), commissioner.Id, league.Id);
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
