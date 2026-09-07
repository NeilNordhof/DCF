namespace DCF.Api.Charts;

/// <summary>
/// A single chart implementation. Adding a new chart means adding one of these and
/// registering it in DI — the request/response mechanism never changes.
/// </summary>
public interface IChartDefinition
{
    /// <summary>Stable identifier clients send to request this chart.</summary>
    string Key { get; }

    string Title { get; }

    ChartKind Kind { get; }

    IReadOnlyList<ChartParameterDescriptor> Parameters { get; }

    /// <summary>True when the chart can only be answered for a league the caller belongs to.</summary>
    bool IsLeagueScoped { get; }

    /// <summary>
    /// Returns the league whose membership is required to view this chart, or null when the
    /// chart is not league scoped. Called before the chart is computed.
    /// </summary>
    Guid? GetLeagueScope(ChartParameters parameters);

    /// <summary>Validates parameters, throwing <see cref="ChartParameterException"/> when invalid.</summary>
    void Validate(ChartParameters parameters);

    Task<ChartResult> ComputeAsync(ChartParameters parameters, CancellationToken cancellationToken);
}

/// <summary>Convenience base handling the common "no league scope / no extra validation" case.</summary>
public abstract class ChartDefinitionBase : IChartDefinition
{
    public abstract string Key { get; }

    public abstract string Title { get; }

    public abstract ChartKind Kind { get; }

    public abstract IReadOnlyList<ChartParameterDescriptor> Parameters { get; }

    public virtual bool IsLeagueScoped => false;

    public virtual Guid? GetLeagueScope(ChartParameters parameters)
    {
        return null;
    }

    public virtual void Validate(ChartParameters parameters)
    {
    }

    public abstract Task<ChartResult> ComputeAsync(ChartParameters parameters, CancellationToken cancellationToken);
}
