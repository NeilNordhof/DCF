using DCF.Api.Charts;
using DCF.Api.Charts.Definitions;
using Xunit;

namespace DCF.Tests.Charts;

public class DciSeasonScoreProgressionChartTests
{
    [Fact]
    public async Task Compute_ReturnsOneSeriesPerCorps_OrderedByShowDate()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_ReturnsOneSeriesPerCorps_OrderedByShowDate));
        var season = db.AddSeason();
        var bluecoats = db.AddCorps("Bluecoats");
        var cavaliers = db.AddCorps("Cavaliers");
        var late = db.AddShow(season, "Finals", new DateOnly(2026, 8, 8));
        var early = db.AddShow(season, "Opener", new DateOnly(2026, 6, 20));

        db.AddTotalScore(bluecoats, early, 70.5);
        db.AddTotalScore(bluecoats, late, 97.1);
        db.AddTotalScore(cavaliers, early, 68.2);
        await db.SaveChangesAsync();

        var chart = new DciSeasonScoreProgressionChart(db);
        var result = await chart.ComputeAsync(
            ChartTestHelpers.Params(
                ("seasonId", season.Id.ToString()),
                ("corpsIds", new[] { bluecoats.Id.ToString(), cavaliers.Id.ToString() })),
            CancellationToken.None);

        Assert.Equal(ChartKind.Line, result.Kind);
        Assert.Equal("2026 Season Score Progression", result.Title);
        Assert.Equal("Show", result.XAxisLabel);
        Assert.Equal("Score", result.YAxisLabel);
        Assert.Equal(2, result.Series.Count);

        var blue = result.Series[0];
        Assert.Equal("Bluecoats", blue.Name);
        Assert.Equal(["Opener", "Finals"], blue.Points.Select(p => p.Label));
        Assert.Equal([70.5, 97.1], blue.Points.Select(p => p.Value));

        var cavs = result.Series[1];
        Assert.Equal("Cavaliers", cavs.Name);

        // Cavaliers skipped Finals — the point is present but has no value.
        Assert.Equal([68.2, null], cavs.Points.Select(p => p.Value));
    }

    [Fact]
    public async Task Compute_UnpublishedSeason_Throws()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_UnpublishedSeason_Throws));
        var season = db.AddSeason(published: false);
        var corps = db.AddCorps("Blue Devils");
        await db.SaveChangesAsync();

        var chart = new DciSeasonScoreProgressionChart(db);

        await Assert.ThrowsAsync<ChartParameterException>(() => chart.ComputeAsync(
            ChartTestHelpers.Params(("seasonId", season.Id.ToString()), ("corpsIds", new[] { corps.Id.ToString() })),
            CancellationToken.None));
    }

    [Fact]
    public async Task Compute_IgnoresOtherSeasonsAndOtherCorps()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_IgnoresOtherSeasonsAndOtherCorps));
        var season = db.AddSeason();
        var other = db.AddSeason(2025);
        var corps = db.AddCorps("Boston Crusaders");
        var rival = db.AddCorps("Crown");
        var show = db.AddShow(season, "Show A", new DateOnly(2026, 7, 1));
        var otherShow = db.AddShow(other, "Old Show", new DateOnly(2025, 7, 1));

        db.AddTotalScore(corps, show, 80);
        db.AddTotalScore(corps, otherShow, 60);
        db.AddTotalScore(rival, show, 85);
        await db.SaveChangesAsync();

        var chart = new DciSeasonScoreProgressionChart(db);
        var result = await chart.ComputeAsync(
            ChartTestHelpers.Params(("seasonId", season.Id.ToString()), ("corpsIds", new[] { corps.Id.ToString() })),
            CancellationToken.None);

        var series = Assert.Single(result.Series);
        var point = Assert.Single(series.Points);
        Assert.Equal("Show A", point.Label);
        Assert.Equal(80, point.Value);
    }

    [Fact]
    public void Validate_MissingCorpsIds_Throws()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Validate_MissingCorpsIds_Throws));
        var chart = new DciSeasonScoreProgressionChart(db);

        Assert.Throws<ChartParameterException>(() =>
            chart.Validate(ChartTestHelpers.Params(("seasonId", Guid.NewGuid().ToString()))));
    }

    [Fact]
    public void GetLeagueScope_IsNull()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetLeagueScope_IsNull));
        var chart = new DciSeasonScoreProgressionChart(db);

        Assert.Null(chart.GetLeagueScope(ChartTestHelpers.Params()));
    }
}
