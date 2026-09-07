using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Services;

internal sealed class SpyDraftServiceForExpiry : IDraftService
{
    public List<(Guid LeagueId, int ExpectedPickNumber)> ExpireCalls { get; } = [];

    public Task ExpireCurrentPickAsync(Guid leagueId, int expectedPickNumber)
    {
        lock (ExpireCalls)
        {
            ExpireCalls.Add((leagueId, expectedPickNumber));
        }

        return Task.CompletedTask;
    }

    public Task PublishStateAsync(Guid leagueId) => Task.CompletedTask;
    public Task OpenDraftAsync(Guid leagueId) => throw new NotImplementedException();
    public Task OpenDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
    public Task StartDraftAsync(Guid leagueId) => throw new NotImplementedException();
    public Task StartDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
    public Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption) => throw new NotImplementedException();
    public Task SkipCurrentPickAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
}

public class PickTimerServiceTests
{
    private static (DcfDbContext Db, PickTimerService Service, SpyDraftServiceForExpiry Spy) Create(string dbName)
    {
        var db = new DcfDbContext(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
        var spy = new SpyDraftServiceForExpiry();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IDraftService>(spy);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var svc = new PickTimerService(scopeFactory, NullLogger<PickTimerService>.Instance);

        return (db, svc, spy);
    }

    [Fact]
    public async Task SchedulePickExpiration_DelayElapses_CallsExpireCurrentPickAsync()
    {
        var (_, svc, spy) = Create(nameof(SchedulePickExpiration_DelayElapses_CallsExpireCurrentPickAsync));
        var leagueId = Guid.NewGuid();

        svc.SchedulePickExpiration(leagueId, pickNumber: 3, DateTimeOffset.UtcNow.AddMilliseconds(50));

        await Task.Delay(300);

        var call = Assert.Single(spy.ExpireCalls);
        Assert.Equal(leagueId, call.LeagueId);
        Assert.Equal(3, call.ExpectedPickNumber);
    }

    [Fact]
    public async Task CancelPickTimer_BeforeDeadline_PreventsExpiration()
    {
        var (_, svc, spy) = Create(nameof(CancelPickTimer_BeforeDeadline_PreventsExpiration));
        var leagueId = Guid.NewGuid();

        svc.SchedulePickExpiration(leagueId, pickNumber: 0, DateTimeOffset.UtcNow.AddMilliseconds(100));
        svc.CancelPickTimer(leagueId);

        await Task.Delay(400);

        Assert.Empty(spy.ExpireCalls);
    }

    [Fact]
    public async Task SchedulePickExpiration_CalledAgainForSameLeague_CancelsThePreviousSchedule()
    {
        var (_, svc, spy) = Create(nameof(SchedulePickExpiration_CalledAgainForSameLeague_CancelsThePreviousSchedule));
        var leagueId = Guid.NewGuid();

        // First schedule would fire at ~300ms if left alone.
        svc.SchedulePickExpiration(leagueId, pickNumber: 0, DateTimeOffset.UtcNow.AddMilliseconds(300));
        // Immediately superseded by a second schedule (e.g. a pick was submitted right away).
        svc.SchedulePickExpiration(leagueId, pickNumber: 1, DateTimeOffset.UtcNow.AddMilliseconds(50));

        await Task.Delay(500);

        // Only the second schedule's expiration should have fired - the first was cancelled, not stacked.
        var call = Assert.Single(spy.ExpireCalls);
        Assert.Equal(1, call.ExpectedPickNumber);
    }

    [Fact]
    public async Task ExecuteAsync_ReloadsInProgressLeagueWithPickDeadline_ResumesExpiration()
    {
        var (db, svc, spy) = Create(nameof(ExecuteAsync_ReloadsInProgressLeagueWithPickDeadline_ResumesExpiration));
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Resumed League",
            CommissionerUserId = Guid.NewGuid(),
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = "[]",
            CurrentPickNumber = 2,
            InviteCode = "RESUME",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1,
            PickTimerSeconds = 30,
            PickDeadline = DateTimeOffset.UtcNow.AddMilliseconds(50)
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        // BackgroundService.StartAsync runs ExecuteAsync's startup recovery, mirroring what
        // the host does when the API process restarts mid-draft.
        await svc.StartAsync(CancellationToken.None);

        await Task.Delay(300);

        var call = Assert.Single(spy.ExpireCalls);
        Assert.Equal(league.Id, call.LeagueId);
        Assert.Equal(2, call.ExpectedPickNumber);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresLeaguesWithoutAnActivePickDeadline()
    {
        var (db, svc, spy) = Create(nameof(ExecuteAsync_IgnoresLeaguesWithoutAnActivePickDeadline));
        db.Leagues.Add(new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "No Timer League",
            CommissionerUserId = Guid.NewGuid(),
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = "[]",
            InviteCode = "NOTIMER",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1,
            PickTimerSeconds = 0,
            PickDeadline = null
        });
        await db.SaveChangesAsync();

        await svc.StartAsync(CancellationToken.None);

        await Task.Delay(200);

        Assert.Empty(spy.ExpireCalls);
    }
}
