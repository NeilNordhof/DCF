using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Charts.Definitions;

/// <summary>
/// One or more league members' current fantasy score, split into the league's drafted captions.
/// Values are weighted caption contributions, so a member's points sum to their standings score.
/// </summary>
public class FantasyLeagueCaptionBreakdownChart(DcfDbContext db, IStandingsService standingsService) : ChartDefinitionBase
{
    public const string ChartKey = "fantasy-league-caption-breakdown";

    public override string Key => ChartKey;

    public override string Title => "Fantasy Caption Breakdown";

    public override ChartKind Kind => ChartKind.Bar;

    public override IReadOnlyList<ChartParameterDescriptor> Parameters =>
    [
        new("leagueId", ChartParameterKind.Guid, true, "League whose members are charted."),
        new("userIds", ChartParameterKind.GuidList, false, "Members to plot as series; defaults to every member.")
    ];

    public override bool IsLeagueScoped => true;

    public override Guid? GetLeagueScope(ChartParameters parameters)
    {
        return parameters.GetGuid("leagueId");
    }

    public override void Validate(ChartParameters parameters)
    {
        parameters.GetGuid("leagueId");
        parameters.GetGuidList("userIds", required: false);
    }

    public override async Task<ChartResult> ComputeAsync(ChartParameters parameters, CancellationToken cancellationToken)
    {
        var leagueId = parameters.GetGuid("leagueId");
        var userIds = parameters.GetGuidList("userIds", required: false);

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId, cancellationToken)
            ?? throw new ChartParameterException("League not found.");

        var breakdowns = await standingsService.GetScoreBreakdownAsync(leagueId);

        if (userIds.Count > 0)
        {
            breakdowns = breakdowns.Where(b => userIds.Contains(b.UserId)).ToList();
        }

        var captions = league.DraftableCaptions;

        var series = breakdowns
            .Select(member => new ChartSeries(
                member.DisplayName,
                captions
                    .Select(caption => new ChartPoint(
                        FormatCaption(caption),
                        member.Captions.TryGetValue(caption, out var breakdown)
                            ? Math.Round(breakdown.Avg * StandingsService.GetWeight(caption, captions), 3)
                            : null))
                    .ToList()))
            .ToList();

        return new ChartResult(
            Key,
            Kind,
            $"{league.Name} Caption Breakdown",
            "Caption",
            "Fantasy points",
            series);
    }

    public static string FormatCaption(ComputedCaption caption)
    {
        var name = caption.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && StartsWord(name[i]) && !StartsWord(name[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    private static bool StartsWord(char value)
    {
        return char.IsUpper(value) || char.IsDigit(value);
    }
}
