namespace DCF.Api.Charts;

/// <summary>
/// Drains the chart job queue, computing each request on a background scope so HTTP callers
/// never wait on the (potentially expensive) computation.
/// </summary>
public class ChartJobWorker(
    IChartJobQueue queue,
    IChartJobStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<ChartJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(jobId, stoppingToken);
        }
    }

    /// <summary>Computes a single queued job and records its outcome on the store.</summary>
    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = store.Get(jobId);

        if (job is null)
        {
            return;
        }

        store.Update(jobId, j => j.Status = ChartJobStatus.Running);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IChartEngine>();
            var result = await engine.ComputeAsync(job.ChartKey, job.Parameters, job.UserId, cancellationToken);

            store.Update(jobId, j =>
            {
                j.Result = result;
                j.Status = ChartJobStatus.Succeeded;
                j.CompletedAt = DateTimeOffset.UtcNow;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            store.Update(jobId, j =>
            {
                j.Status = ChartJobStatus.Failed;
                j.Error = "Chart computation was cancelled.";
                j.CompletedAt = DateTimeOffset.UtcNow;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chart job {JobId} ({ChartKey}) failed", jobId, job.ChartKey);

            store.Update(jobId, j =>
            {
                j.Status = ChartJobStatus.Failed;
                j.Error = ex is ChartParameterException or UnauthorizedAccessException
                    ? ex.Message
                    : "Chart computation failed.";
                j.CompletedAt = DateTimeOffset.UtcNow;
            });
        }
    }
}
