using DCF.Data.Entities;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class ScrapeSchedulerService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void ScheduleScrape(ShowEntity show) { }
    public Task ExecuteScrapeAsync(ShowEntity show) => Task.CompletedTask;
}
