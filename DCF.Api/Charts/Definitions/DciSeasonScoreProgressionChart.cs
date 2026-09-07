using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Charts.Definitions;

/// <summary>
/// One or more corps' real DCI total scores across every show of a season, ordered by date.
/// </summary>
public class DciSeasonScoreProgressionChart(DcfDbContext db) : ChartDefinitionBase
{
    public const string ChartKey = "dci-season-score-progression";

    public override string Key => ChartKey;

    public override string Title => "Season Score Progression";

    public override ChartKind Kind => ChartKind.Line;

    public override IReadOnlyList<ChartParameterDescriptor> Parameters =>
    [
        new("seasonId", ChartParameterKind.Guid, true, "Season to chart."),
        new("corpsIds", ChartParameterKind.GuidList, true, "One or more corps to plot as series.")
    ];

    public override void Validate(ChartParameters parameters)
    {
        parameters.GetGuid("seasonId");
        parameters.GetGuidList("corpsIds", required: true);
    }

    public override async Task<ChartResult> ComputeAsync(ChartParameters parameters, CancellationToken cancellationToken)
    {
        var seasonId = parameters.GetGuid("seasonId");
        var corpsIds = parameters.GetGuidList("corpsIds", required: true);

        var season = await db.Seasons
            .FirstOrDefaultAsync(s => s.Id == seasonId && s.IsPublished, cancellationToken)
            ?? throw new ChartParameterException("Season not found.");

        var rows = await db.Scores
            .Where(s => s.Caption == Caption.Total &&
                        s.Show.SeasonId == seasonId &&
                        corpsIds.Contains(s.CorpsId))
            .Select(s => new
            {
                s.CorpsId,
                CorpsName = s.Corps.Name,
                s.ShowId,
                ShowName = s.Show.Name,
                s.Show.Date,
                s.TotalScore
            })
            .ToListAsync(cancellationToken);

        var corpsNames = await db.Corps
            .Where(c => corpsIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Shared x-axis: every show (in date order) at which any requested corps scored.
        var shows = rows
            .GroupBy(r => r.ShowId)
            .Select(g => new { ShowId = g.Key, g.First().ShowName, g.First().Date })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.ShowName, StringComparer.Ordinal)
            .ToList();

        var scores = rows.ToDictionary(r => (r.CorpsId, r.ShowId), r => r.TotalScore);

        var series = corpsIds
            .Where(corpsNames.ContainsKey)
            .Select(corpsId => new ChartSeries(
                corpsNames[corpsId],
                shows
                    .Select(show => new ChartPoint(
                        show.ShowName,
                        scores.TryGetValue((corpsId, show.ShowId), out var score) ? score : null))
                    .ToList()))
            .ToList();

        return new ChartResult(
            Key,
            Kind,
            $"{season.Year} Season Score Progression",
            "Show",
            "Score",
            series);
    }
}
