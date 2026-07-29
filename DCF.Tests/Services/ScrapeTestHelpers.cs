using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DCF.Tests.Services;

internal sealed class NullMqttService : IMqttService
{
    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class FakeRecapScraperTask(
    int failuresBeforeSuccess = int.MaxValue,
    Func<Show, List<Result>>? onSuccess = null) : IRecapScraperTask
{
    public int CallCount { get; private set; }

    public Task<List<Result>> ScrapeAsync(Show show)
    {
        CallCount++;

        if (CallCount <= failuresBeforeSuccess)
        {
            throw new InvalidOperationException("Simulated scrape failure");
        }

        var results = onSuccess?.Invoke(show) ?? [];

        return Task.FromResult(results);
    }
}

internal sealed class FakeShowInfoScraperTask(ShowPrefillData? result) : IShowInfoScraperTask
{
    public Task<ShowPrefillData?> ScrapeAsync(string url)
    {
        return Task.FromResult(result);
    }
}

internal sealed class RecordingEmailService : IEmailService
{
    public List<string> SentToEmails { get; } = [];

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        SentToEmails.Add(toEmail);

        return Task.CompletedTask;
    }
}

internal static class ScrapeTestHelpers
{
    public static ScrapeSchedulerService CreateSvc(
        DcfDbContext db,
        IRecapScraperTask scraperTask,
        Dictionary<string, string?>? configValues = null,
        IEmailService? emailService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(scraperTask);
        services.AddSingleton(emailService ?? new NullEmailService());

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();

        var emailOpts = Options.Create(new EmailOptions { UnsubscribeSecret = "test-secret", FrontendUrl = "http://test.local" });
        var tokenSvc = new EmailTokenService(emailOpts);

        return new ScrapeSchedulerService(
            scopeFactory,
            new NullMqttService(),
            config,
            emailOpts,
            tokenSvc,
            NullLogger<ScrapeSchedulerService>.Instance);
    }
}
