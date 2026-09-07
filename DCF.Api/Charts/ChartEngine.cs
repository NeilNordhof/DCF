using DCF.Data;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Charts;

/// <summary>
/// Generic dispatcher: resolves a chart by key, validates its parameters, enforces
/// league-membership scoping, and produces the standardized <see cref="ChartResult"/>.
/// </summary>
public class ChartEngine(IEnumerable<IChartDefinition> definitions, DcfDbContext db) : IChartEngine
{
    private readonly Dictionary<string, IChartDefinition> definitions =
        definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ChartDefinitionInfo> GetDefinitions()
    {
        return this.definitions.Values
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new ChartDefinitionInfo(
                d.Key,
                d.Title,
                d.Kind,
                d.IsLeagueScoped,
                d.Parameters))
            .ToList();
    }

    public async Task<ChartValidation> ValidateAsync(
        string chartKey, ChartParameters parameters, Guid userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chartKey) || !definitions.TryGetValue(chartKey, out var definition))
        {
            return new ChartValidation(ChartRequestStatus.UnknownChart, $"Unknown chart '{chartKey}'.");
        }

        Guid? leagueId;

        try
        {
            definition.Validate(parameters);
            leagueId = definition.GetLeagueScope(parameters);
        }
        catch (ChartParameterException ex)
        {
            return new ChartValidation(ChartRequestStatus.InvalidParameters, ex.Message);
        }

        if (leagueId is not null)
        {
            var isMember = await db.LeagueMembers
                .AnyAsync(m => m.LeagueId == leagueId.Value && m.UserId == userId, cancellationToken);

            if (!isMember)
            {
                // Mirrors LeagueService's own !isMember && !league.IsPublic gate - a public
                // league's charts are visible the same way its standings/roster already are.
                var isPublic = await db.Leagues
                    .Where(l => l.Id == leagueId.Value)
                    .Select(l => l.IsPublic)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!isPublic)
                {
                    return new ChartValidation(
                        ChartRequestStatus.Forbidden, "You are not a member of that league.");
                }
            }
        }

        return ChartValidation.Ok;
    }

    public async Task<ChartResult> ComputeAsync(
        string chartKey, ChartParameters parameters, Guid userId, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(chartKey, parameters, userId, cancellationToken);

        switch (validation.Status)
        {
            case ChartRequestStatus.UnknownChart:
            case ChartRequestStatus.InvalidParameters:
                throw new ChartParameterException(validation.Error ?? "Invalid chart request.");
            case ChartRequestStatus.Forbidden:
                throw new UnauthorizedAccessException(validation.Error ?? "Forbidden.");
        }

        return await definitions[chartKey].ComputeAsync(parameters, cancellationToken);
    }
}
