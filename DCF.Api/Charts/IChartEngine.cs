namespace DCF.Api.Charts;

public enum ChartRequestStatus
{
    Ok,
    UnknownChart,
    InvalidParameters,
    Forbidden,
}

public record ChartValidation(ChartRequestStatus Status, string? Error = null)
{
    public bool IsValid => Status == ChartRequestStatus.Ok;

    public static ChartValidation Ok { get; } = new(ChartRequestStatus.Ok);
}

public interface IChartEngine
{
    IReadOnlyList<ChartDefinitionInfo> GetDefinitions();

    Task<ChartValidation> ValidateAsync(string chartKey, ChartParameters parameters, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Validates then computes. Throws <see cref="ChartParameterException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the request is not answerable.</summary>
    Task<ChartResult> ComputeAsync(string chartKey, ChartParameters parameters, Guid userId, CancellationToken cancellationToken = default);
}
