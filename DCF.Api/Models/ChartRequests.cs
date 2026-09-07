using System.Text.Json;
using DCF.Api.Charts;

namespace DCF.Api.Models;

/// <summary>
/// Generic chart request envelope — the transport never changes when charts are added,
/// only the <see cref="ChartKey"/> and the loosely-typed <see cref="Parameters"/> bag do.
/// </summary>
public record ChartRequest(string ChartKey, Dictionary<string, JsonElement>? Parameters);

public record ChartJobResponse(
    Guid JobId,
    string ChartKey,
    ChartJobStatus Status,
    ChartResult? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ChartJobResponse From(ChartJob job)
    {
        return new ChartJobResponse(
            job.Id, job.ChartKey, job.Status, job.Result, job.Error, job.CreatedAt, job.CompletedAt);
    }
}
