using DCF.Api.Charts;
using DCF.Api.Charts.Definitions;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Xunit;

namespace DCF.Tests.Charts;

public class FantasyLeagueCaptionBreakdownChartTests
{
    private static readonly ComputedCaption[] Captions = [ComputedCaption.Brass, ComputedCaption.Percussion];

    private static (LeagueEntity League, UserEntity Owner, UserEntity Rival) Seed(DcfDbContext db)
    {
        var season = db.AddSeason();
        var owner = db.AddUser("auth0|owner", "Owner");
        var rival = db.AddUser("auth0|rival", "Rival");
        var league = db.AddLeague(season, owner, Captions);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = rival.Id });

        var blue = db.AddCorps("Bluecoats");
        var cavs = db.AddCorps("Cavaliers");
        var show = db.AddShow(season, "Finals", new DateOnly(2026, 8, 8));

        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = season.Id,
            CorpsId = blue.Id,
            Brass = 20,
            Percussion = 10
        });
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = season.Id,
            CorpsId = cavs.Id,
            Brass = 16,
            Percussion = 18
        });

        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = owner.Id, CorpsId = blue.Id, Caption = ComputedCaption.Brass
        });
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = owner.Id, CorpsId = cavs.Id, Caption = ComputedCaption.Percussion
        });
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = rival.Id, CorpsId = cavs.Id, Caption = ComputedCaption.Brass
        });

        db.SaveChanges();

        return (league, owner, rival);
    }

    private static FantasyLeagueCaptionBreakdownChart CreateChart(DcfDbContext db)
    {
        return new FantasyLeagueCaptionBreakdownChart(db, new StandingsService(db));
    }

    [Fact]
    public async Task Compute_ReturnsSeriesPerMemberWithWeightedCaptionPoints()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_ReturnsSeriesPerMemberWithWeightedCaptionPoints));
        var (league, _, _) = Seed(db);

        var result = await CreateChart(db).ComputeAsync(
            ChartTestHelpers.Params(("leagueId", league.Id.ToString())), CancellationToken.None);

        Assert.Equal(ChartKind.Bar, result.Kind);
        Assert.Equal("Test League Caption Breakdown", result.Title);
        Assert.Equal("Caption", result.XAxisLabel);
        Assert.Equal("Fantasy points", result.YAxisLabel);
        Assert.Equal(2, result.Series.Count);

        var owner = Assert.Single(result.Series, s => s.Name == "Owner");
        Assert.Equal(["Brass", "Percussion"], owner.Points.Select(p => p.Label));

        var weight = StandingsService.GetWeight(ComputedCaption.Brass, Captions);
        Assert.Equal(20 * weight, owner.Points[0].Value);
        Assert.Equal(18 * weight, owner.Points[1].Value);

        var rival = Assert.Single(result.Series, s => s.Name == "Rival");
        Assert.Equal(16 * weight, rival.Points[0].Value);
        Assert.Equal(0, rival.Points[1].Value);
    }

    [Fact]
    public async Task Compute_FiltersToRequestedMembers()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_FiltersToRequestedMembers));
        var (league, _, rival) = Seed(db);

        var result = await CreateChart(db).ComputeAsync(
            ChartTestHelpers.Params(("leagueId", league.Id.ToString()), ("userIds", new[] { rival.Id.ToString() })),
            CancellationToken.None);

        var series = Assert.Single(result.Series);
        Assert.Equal("Rival", series.Name);
    }

    [Fact]
    public async Task Compute_UnknownLeague_Throws()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(Compute_UnknownLeague_Throws));

        await Assert.ThrowsAsync<ChartParameterException>(() => CreateChart(db).ComputeAsync(
            ChartTestHelpers.Params(("leagueId", Guid.NewGuid().ToString())), CancellationToken.None));
    }

    [Fact]
    public void GetLeagueScope_ReturnsLeagueId()
    {
        using var db = ChartTestHelpers.CreateDb(nameof(GetLeagueScope_ReturnsLeagueId));
        var leagueId = Guid.NewGuid();

        Assert.Equal(
            leagueId,
            CreateChart(db).GetLeagueScope(ChartTestHelpers.Params(("leagueId", leagueId.ToString()))));
    }

    [Theory]
    [InlineData(ComputedCaption.GeneralEffectCombined, "General Effect Combined")]
    [InlineData(ComputedCaption.GeneralEffect1, "General Effect 1")]
    [InlineData(ComputedCaption.Brass, "Brass")]
    public void FormatCaption_SplitsPascalCase(ComputedCaption caption, string expected)
    {
        Assert.Equal(expected, FantasyLeagueCaptionBreakdownChart.FormatCaption(caption));
    }
}
