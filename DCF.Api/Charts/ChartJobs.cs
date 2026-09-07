using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace DCF.Api.Charts;

public enum ChartJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}

/// <summary>A single asynchronous chart computation, polled by the client until terminal.</summary>
public class ChartJob
{
    public required Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required string ChartKey { get; init; }

    [JsonIgnore]
    public required ChartParameters Parameters { get; init; }

    public ChartJobStatus Status { get; set; } = ChartJobStatus.Pending;

    public ChartResult? Result { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsTerminal => Status is ChartJobStatus.Succeeded or ChartJobStatus.Failed;
}

public interface IChartJobStore
{
    ChartJob Create(Guid userId, string chartKey, ChartParameters parameters);

    ChartJob? Get(Guid jobId);

    void Update(Guid jobId, Action<ChartJob> mutate);
}

/// <summary>
/// In-memory job store. Chart results are cheap to recompute and short-lived, so completed
/// jobs are pruned rather than persisted.
/// </summary>
public class InMemoryChartJobStore(TimeProvider? timeProvider = null) : IChartJobStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, ChartJob> jobs = new();
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public ChartJob Create(Guid userId, string chartKey, ChartParameters parameters)
    {
        Prune();

        var job = new ChartJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChartKey = chartKey,
            Parameters = parameters,
            CreatedAt = time.GetUtcNow()
        };

        jobs[job.Id] = job;

        return job;
    }

    public ChartJob? Get(Guid jobId)
    {
        return jobs.GetValueOrDefault(jobId);
    }

    public void Update(Guid jobId, Action<ChartJob> mutate)
    {
        if (jobs.TryGetValue(jobId, out var job))
        {
            lock (job)
            {
                mutate(job);
            }
        }
    }

    private void Prune()
    {
        var cutoff = time.GetUtcNow() - Retention;

        foreach (var (id, job) in jobs)
        {
            if (job.IsTerminal && job.CompletedAt is { } completed && completed < cutoff)
            {
                jobs.TryRemove(id, out _);
            }
        }
    }
}
