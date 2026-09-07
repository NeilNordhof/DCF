using System.Security.Claims;
using System.Text.Json;
using DCF.Api.Charts;
using DCF.Api.Charts.Definitions;
using DCF.Api.Controllers;
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DCF.Tests.Charts;

public class ChartsControllerTests
{
    private static ChartsController CreateController(DcfDbContext db, string sub, IChartJobStore store, IChartJobQueue queue)
    {
        IChartDefinition[] definitions =
        [
            new DciSeasonScoreProgressionChart(db),
            new FantasyLeagueCaptionBreakdownChart(db, new StandingsService(db))
        ];

        var controller = new ChartsController(new ChartEngine(definitions, db), store, queue, new UserService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sub)], "Test"))
                }
            }
        };

        return controller;
    }

    private static Dictionary<string, JsonElement> Parameters(params (string Key, object? Value)[] values)
    {
        return values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));
    }

    [Fact]
    public void GetDefinitions_ReturnsCatalog()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetDefinitions_ReturnsCatalog));
        var controller = CreateController(db, "auth0|x", new InMemoryChartJobStore(), new ChartJobQueue());

        var ok = Assert.IsType<OkObjectResult>(controller.GetDefinitions());
        var definitions = Assert.IsAssignableFrom<IReadOnlyList<ChartDefinitionInfo>>(ok.Value);

        Assert.Contains(definitions, d => d.Key == DciSeasonScoreProgressionChart.ChartKey);
        Assert.Contains(definitions, d => d.Key == FantasyLeagueCaptionBreakdownChart.ChartKey);
    }

    [Fact]
    public async Task Submit_ValidRequest_QueuesJobAndReturnsAccepted()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_ValidRequest_QueuesJobAndReturnsAccepted));
        var user = db.AddUser("auth0|user", "User");
        var season = db.AddSeason();
        var corps = db.AddCorps("Blue Devils");
        await db.SaveChangesAsync();

        var store = new InMemoryChartJobStore();
        var queue = new ChartJobQueue();
        var controller = CreateController(db, "auth0|user", store, queue);

        var result = await controller.Submit(
            new ChartRequest(
                DciSeasonScoreProgressionChart.ChartKey,
                Parameters(("seasonId", season.Id.ToString()), ("corpsIds", new[] { corps.Id.ToString() }))),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        var body = Assert.IsType<ChartJobResponse>(accepted.Value);

        Assert.Equal(ChartJobStatus.Pending, body.Status);
        Assert.Null(body.Result);
        Assert.Equal(user.Id, store.Get(body.JobId)!.UserId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queued = new List<Guid>();

        await foreach (var id in queue.DequeueAllAsync(cts.Token))
        {
            queued.Add(id);

            break;
        }

        Assert.Equal([body.JobId], queued);
    }

    [Fact]
    public async Task Submit_UnknownChart_ReturnsNotFound()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_UnknownChart_ReturnsNotFound));
        db.AddUser("auth0|user", "User");
        await db.SaveChangesAsync();

        var controller = CreateController(db, "auth0|user", new InMemoryChartJobStore(), new ChartJobQueue());

        var result = await controller.Submit(new ChartRequest("nope", null), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Submit_MissingParameters_ReturnsBadRequest()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_MissingParameters_ReturnsBadRequest));
        db.AddUser("auth0|user", "User");
        await db.SaveChangesAsync();

        var controller = CreateController(db, "auth0|user", new InMemoryChartJobStore(), new ChartJobQueue());

        var result = await controller.Submit(
            new ChartRequest(DciSeasonScoreProgressionChart.ChartKey, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Submit_LeagueChartForNonMember_IsForbidden()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_LeagueChartForNonMember_IsForbidden));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass]);
        db.AddUser("auth0|outsider", "Outsider");
        await db.SaveChangesAsync();

        var store = new InMemoryChartJobStore();
        var controller = CreateController(db, "auth0|outsider", store, new ChartJobQueue());

        var result = await controller.Submit(
            new ChartRequest(FantasyLeagueCaptionBreakdownChart.ChartKey, Parameters(("leagueId", league.Id.ToString()))),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Submit_LeagueChartForNonMemberOfPublicLeague_IsAccepted()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_LeagueChartForNonMemberOfPublicLeague_IsAccepted));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass], isPublic: true);
        db.AddUser("auth0|outsider", "Outsider");
        await db.SaveChangesAsync();

        var controller = CreateController(db, "auth0|outsider", new InMemoryChartJobStore(), new ChartJobQueue());

        var result = await controller.Submit(
            new ChartRequest(FantasyLeagueCaptionBreakdownChart.ChartKey, Parameters(("leagueId", league.Id.ToString()))),
            CancellationToken.None);

        Assert.IsType<AcceptedAtActionResult>(result);
    }

    [Fact]
    public async Task Submit_LeagueChartForMember_IsAccepted()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_LeagueChartForMember_IsAccepted));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass]);
        await db.SaveChangesAsync();

        var controller = CreateController(db, "auth0|comm", new InMemoryChartJobStore(), new ChartJobQueue());

        var result = await controller.Submit(
            new ChartRequest(FantasyLeagueCaptionBreakdownChart.ChartKey, Parameters(("leagueId", league.Id.ToString()))),
            CancellationToken.None);

        Assert.IsType<AcceptedAtActionResult>(result);
    }

    [Fact]
    public async Task Submit_UnknownUser_ReturnsUnauthorized()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Submit_UnknownUser_ReturnsUnauthorized));

        var controller = CreateController(db, "auth0|ghost", new InMemoryChartJobStore(), new ChartJobQueue());

        var result = await controller.Submit(new ChartRequest("nope", null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetJob_OwnJob_ReturnsStatusAndResult()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetJob_OwnJob_ReturnsStatusAndResult));
        var user = db.AddUser("auth0|user", "User");
        await db.SaveChangesAsync();

        var store = new InMemoryChartJobStore();
        var job = store.Create(user.Id, "stub", ChartTestHelpers.Params());
        var chartResult = new ChartResult("stub", ChartKind.BoxAndWhisker, "T", "X", "Y", []);

        store.Update(job.Id, j =>
        {
            j.Status = ChartJobStatus.Succeeded;
            j.Result = chartResult;
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var controller = CreateController(db, "auth0|user", store, new ChartJobQueue());

        var ok = Assert.IsType<OkObjectResult>(await controller.GetJob(job.Id));
        var body = Assert.IsType<ChartJobResponse>(ok.Value);

        Assert.Equal(ChartJobStatus.Succeeded, body.Status);
        Assert.Same(chartResult, body.Result);
    }

    [Fact]
    public async Task GetJob_OtherUsersJob_ReturnsNotFound()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetJob_OtherUsersJob_ReturnsNotFound));
        db.AddUser("auth0|user", "User");
        var other = db.AddUser("auth0|other", "Other");
        await db.SaveChangesAsync();

        var store = new InMemoryChartJobStore();
        var job = store.Create(other.Id, "stub", ChartTestHelpers.Params());
        var controller = CreateController(db, "auth0|user", store, new ChartJobQueue());

        Assert.IsType<NotFoundResult>(await controller.GetJob(job.Id));
    }

    [Fact]
    public async Task GetJob_UnknownJob_ReturnsNotFound()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetJob_UnknownJob_ReturnsNotFound));
        db.AddUser("auth0|user", "User");
        await db.SaveChangesAsync();

        var controller = CreateController(db, "auth0|user", new InMemoryChartJobStore(), new ChartJobQueue());

        Assert.IsType<NotFoundResult>(await controller.GetJob(Guid.NewGuid()));
    }
}
