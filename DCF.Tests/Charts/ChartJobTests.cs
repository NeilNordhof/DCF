using DCF.Api.Charts;
using DCF.Api.Charts.Definitions;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Charts;

public class ChartJobTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();

        services.AddDbContext<DcfDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddScoped<IStandingsService, StandingsService>();
        services.AddScoped<IChartDefinition, DciSeasonScoreProgressionChart>();
        services.AddScoped<IChartDefinition, FantasyLeagueCaptionBreakdownChart>();
        services.AddScoped<IChartEngine, ChartEngine>();

        return services.BuildServiceProvider();
    }

    private static ChartJobWorker CreateWorker(ServiceProvider provider, IChartJobStore store)
    {
        return new ChartJobWorker(
            new ChartJobQueue(),
            store,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChartJobWorker>.Instance);
    }

    [Fact]
    public void Store_CreatesPendingJobsAndKeepsThemRetrievable()
    {
        var store = new InMemoryChartJobStore();
        var userId = Guid.NewGuid();

        var job = store.Create(userId, "stub", ChartTestHelpers.Params(("a", 1)));

        Assert.Equal(ChartJobStatus.Pending, job.Status);
        Assert.False(job.IsTerminal);
        Assert.Equal(userId, store.Get(job.Id)!.UserId);
        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Store_Update_MutatesJob()
    {
        var store = new InMemoryChartJobStore();
        var job = store.Create(Guid.NewGuid(), "stub", ChartTestHelpers.Params());

        store.Update(job.Id, j => j.Status = ChartJobStatus.Running);
        store.Update(Guid.NewGuid(), _ => throw new InvalidOperationException("must not run"));

        Assert.Equal(ChartJobStatus.Running, store.Get(job.Id)!.Status);
    }

    [Fact]
    public async Task Queue_RoundTripsJobIds()
    {
        var queue = new ChartJobQueue();
        var id = Guid.NewGuid();

        await queue.EnqueueAsync(id);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var dequeued in queue.DequeueAllAsync(cts.Token))
        {
            Assert.Equal(id, dequeued);

            break;
        }
    }

    [Fact]
    public async Task Worker_ComputesQueuedJob_AndStoresResult()
    {
        await using var provider = BuildProvider(nameof(Worker_ComputesQueuedJob_AndStoresResult));

        Guid seasonId;
        Guid corpsId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
            var season = db.AddSeason();
            var corps = db.AddCorps("Blue Devils");
            var show = db.AddShow(season, "Finals", new DateOnly(2026, 8, 8));
            db.AddTotalScore(corps, show, 99.1);
            await db.SaveChangesAsync();
            seasonId = season.Id;
            corpsId = corps.Id;
        }

        var store = new InMemoryChartJobStore();
        var job = store.Create(
            Guid.NewGuid(),
            DciSeasonScoreProgressionChart.ChartKey,
            ChartTestHelpers.Params(("seasonId", seasonId.ToString()), ("corpsIds", new[] { corpsId.ToString() })));

        await CreateWorker(provider, store).ProcessAsync(job.Id, CancellationToken.None);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ChartJobStatus.Succeeded, finished.Status);
        Assert.True(finished.IsTerminal);
        Assert.NotNull(finished.CompletedAt);
        Assert.Null(finished.Error);
        Assert.Equal(99.1, finished.Result!.Series[0].Points[0].Value);
    }

    [Fact]
    public async Task Worker_ForbiddenLeagueChart_FailsJobWithMessage()
    {
        await using var provider = BuildProvider(nameof(Worker_ForbiddenLeagueChart_FailsJobWithMessage));

        Guid leagueId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
            var season = db.AddSeason();
            var commissioner = db.AddUser("auth0|comm", "Comm");
            leagueId = db.AddLeague(season, commissioner, [ComputedCaption.Brass]).Id;
            await db.SaveChangesAsync();
        }

        var store = new InMemoryChartJobStore();
        var job = store.Create(
            Guid.NewGuid(),
            FantasyLeagueCaptionBreakdownChart.ChartKey,
            ChartTestHelpers.Params(("leagueId", leagueId.ToString())));

        await CreateWorker(provider, store).ProcessAsync(job.Id, CancellationToken.None);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ChartJobStatus.Failed, finished.Status);
        Assert.Equal("You are not a member of that league.", finished.Error);
        Assert.Null(finished.Result);
    }

    [Fact]
    public async Task Worker_BackgroundLoop_DrainsQueuedJobs()
    {
        await using var provider = BuildProvider(nameof(Worker_BackgroundLoop_DrainsQueuedJobs));

        Guid seasonId;
        Guid corpsId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
            var season = db.AddSeason();
            var corps = db.AddCorps("Blue Devils");
            var show = db.AddShow(season, "Finals", new DateOnly(2026, 8, 8));
            db.AddTotalScore(corps, show, 98.4);
            await db.SaveChangesAsync();
            seasonId = season.Id;
            corpsId = corps.Id;
        }

        var store = new InMemoryChartJobStore();
        var queue = new ChartJobQueue();
        var worker = new ChartJobWorker(
            queue, store, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ChartJobWorker>.Instance);

        var job = store.Create(
            Guid.NewGuid(),
            DciSeasonScoreProgressionChart.ChartKey,
            ChartTestHelpers.Params(("seasonId", seasonId.ToString()), ("corpsIds", new[] { corpsId.ToString() })));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await queue.EnqueueAsync(job.Id);

            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!store.Get(job.Id)!.IsTerminal && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var finished = store.Get(job.Id)!;
        Assert.Equal(ChartJobStatus.Succeeded, finished.Status);
        Assert.Equal(98.4, finished.Result!.Series[0].Points[0].Value);
    }

    [Fact]
    public async Task Worker_UnknownJobId_DoesNothing()
    {
        await using var provider = BuildProvider(nameof(Worker_UnknownJobId_DoesNothing));
        var store = new InMemoryChartJobStore();

        await CreateWorker(provider, store).ProcessAsync(Guid.NewGuid(), CancellationToken.None);
    }
}
