namespace DCF.Api.Charts;

/// <summary>Rendering hint for the front end. New kinds can be added without touching transport.</summary>
public enum ChartKind
{
    Line,
    Scatter,
    Bar,
    BoxAndWhisker,
}

/// <summary>
/// A single label/value pair inside a series. <see cref="Value"/> is null when a point is
/// known but has no data (e.g. a corps that did not compete at a show).
/// Box-and-whisker charts additionally populate the five-number summary fields.
/// </summary>
public record ChartPoint(
    string Label,
    double? Value,
    double? Minimum = null,
    double? LowerQuartile = null,
    double? Median = null,
    double? UpperQuartile = null,
    double? Maximum = null);

public record ChartSeries(string Name, IReadOnlyList<ChartPoint> Points);

/// <summary>The standardized, front-end renderable shape every chart returns.</summary>
public record ChartResult(
    string ChartKey,
    ChartKind Kind,
    string Title,
    string XAxisLabel,
    string YAxisLabel,
    IReadOnlyList<ChartSeries> Series);

public enum ChartParameterKind
{
    Guid,
    GuidList,
    Integer,
    String,
    Boolean,
}

public record ChartParameterDescriptor(string Name, ChartParameterKind Kind, bool Required, string Description);

public record ChartDefinitionInfo(
    string Key,
    string Title,
    ChartKind Kind,
    bool IsLeagueScoped,
    IReadOnlyList<ChartParameterDescriptor> Parameters);
