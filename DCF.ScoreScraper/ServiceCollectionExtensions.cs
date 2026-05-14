using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Services;
using DCF.ScoreScraper.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DCF.ScoreScraper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScoreScraper(
        this IServiceCollection services,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyDictionary<string, Corps>>> corpsFactory)
    {
        services.AddSingleton<ICorpsService>(sp =>
            new CorpsService(ct => corpsFactory(sp, ct)));
        services.AddHttpClient<IRecapScraperTask, RecapScraperTask>();
        return services;
    }
}
