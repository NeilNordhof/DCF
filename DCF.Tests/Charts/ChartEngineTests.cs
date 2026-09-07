using DCF.Api.Charts;
using DCF.Api.Charts.Definitions;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Xunit;

namespace DCF.Tests.Charts;

public class ChartEngineTests
{
    private sealed class StubChart : ChartDefinitionBase
    {
        public override string Key => "stub";

        public override string Title => "Stub";

        public override ChartKind Kind => ChartKind.Scatter;

        public override IReadOnlyList<ChartParameterDescriptor> Parameters =>
            [new("value", ChartParameterKind.Integer, false, "Value")];

        public int ComputeCount { get; private set; }

        public override Task<ChartResult> ComputeAsync(ChartParameters parameters, CancellationToken cancellationToken)
        {
            ComputeCount++;

            return Task.FromResult(new ChartResult(
                Key, Kind, "Stub", "X", "Y",
                [new ChartSeries("only", [new ChartPoint("a", parameters.GetInt("value") ?? 1)])]));
        }
    }

    private static ChartEngine CreateEngine(DcfDbContext db, params IChartDefinition[] extra)
    {
        IChartDefinition[] definitions =
        [
            new DciSeasonScoreProgressionChart(db),
            new FantasyLeagueCaptionBreakdownChart(db, new StandingsService(db)),
            .. extra
        ];

        return new ChartEngine(definitions, db);
    }

    [Fact]
    public void GetDefinitions_ListsRegisteredChartsWithMetadata()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetDefinitions_ListsRegisteredChartsWithMetadata));

        var definitions = CreateEngine(db).GetDefinitions();

        Assert.Equal(2, definitions.Count);

        var progression = Assert.Single(definitions, d => d.Key == DciSeasonScoreProgressionChart.ChartKey);
        Assert.Equal(ChartKind.Line, progression.Kind);
        Assert.False(progression.IsLeagueScoped);

        var breakdown = Assert.Single(definitions, d => d.Key == FantasyLeagueCaptionBreakdownChart.ChartKey);
        Assert.Equal(ChartKind.Bar, breakdown.Kind);
        Assert.True(breakdown.IsLeagueScoped);
        Assert.Contains(breakdown.Parameters, p => p.Name == "leagueId" && p.Required);
    }

    [Fact]
    public async Task Validate_UnknownChart_ReturnsUnknownChart()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_UnknownChart_ReturnsUnknownChart));

        var validation = await CreateEngine(db).ValidateAsync("nope", ChartTestHelpers.Params(), Guid.NewGuid());

        Assert.Equal(ChartRequestStatus.UnknownChart, validation.Status);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Validate_MissingParameters_ReturnsInvalidParameters()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_MissingParameters_ReturnsInvalidParameters));

        var validation = await CreateEngine(db).ValidateAsync(
            DciSeasonScoreProgressionChart.ChartKey, ChartTestHelpers.Params(), Guid.NewGuid());

        Assert.Equal(ChartRequestStatus.InvalidParameters, validation.Status);
        Assert.NotNull(validation.Error);
    }

    [Fact]
    public async Task Validate_LeagueChartForNonMember_ReturnsForbidden()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_LeagueChartForNonMember_ReturnsForbidden));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass]);
        var outsider = db.AddUser("auth0|outsider", "Outsider");
        await db.SaveChangesAsync();

        var validation = await CreateEngine(db).ValidateAsync(
            FantasyLeagueCaptionBreakdownChart.ChartKey,
            ChartTestHelpers.Params(("leagueId", league.Id.ToString())),
            outsider.Id);

        Assert.Equal(ChartRequestStatus.Forbidden, validation.Status);
    }

    [Fact]
    public async Task Validate_LeagueChartForNonMemberOfPublicLeague_IsOk()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_LeagueChartForNonMemberOfPublicLeague_IsOk));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass], isPublic: true);
        var outsider = db.AddUser("auth0|outsider", "Outsider");
        await db.SaveChangesAsync();

        var validation = await CreateEngine(db).ValidateAsync(
            FantasyLeagueCaptionBreakdownChart.ChartKey,
            ChartTestHelpers.Params(("leagueId", league.Id.ToString())),
            outsider.Id);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task Validate_LeagueChartForMember_IsOk()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_LeagueChartForMember_IsOk));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass]);
        await db.SaveChangesAsync();

        var validation = await CreateEngine(db).ValidateAsync(
            FantasyLeagueCaptionBreakdownChart.ChartKey,
            ChartTestHelpers.Params(("leagueId", league.Id.ToString())),
            commissioner.Id);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task Compute_NonMember_ThrowsUnauthorized()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_NonMember_ThrowsUnauthorized));
        var season = db.AddSeason();
        var commissioner = db.AddUser("auth0|comm", "Comm");
        var league = db.AddLeague(season, commissioner, [ComputedCaption.Brass]);
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.ComputeAsync(
            FantasyLeagueCaptionBreakdownChart.ChartKey,
            ChartTestHelpers.Params(("leagueId", league.Id.ToString())),
            Guid.NewGuid()));
    }

    [Fact]
    public async Task Compute_UnknownChart_ThrowsChartParameterException()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_UnknownChart_ThrowsChartParameterException));

        await Assert.ThrowsAsync<ChartParameterException>(() =>
            CreateEngine(db).ComputeAsync("nope", ChartTestHelpers.Params(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Compute_DispatchesToDefinition_WithoutTransportChanges()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_DispatchesToDefinition_WithoutTransportChanges));
        var stub = new StubChart();
        var engine = CreateEngine(db, stub);

        var result = await engine.ComputeAsync("STUB", ChartTestHelpers.Params(("value", 42)), Guid.NewGuid());

        Assert.Equal(1, stub.ComputeCount);
        Assert.Equal(ChartKind.Scatter, result.Kind);
        Assert.Equal(42, result.Series[0].Points[0].Value);
        Assert.Equal(3, engine.GetDefinitions().Count);
    }
}
