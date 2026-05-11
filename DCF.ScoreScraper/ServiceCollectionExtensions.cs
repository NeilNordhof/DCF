using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Services;
using DCF.ScoreScraper.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DCF.ScoreScraper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScoreScraper(
        this IServiceCollection services,
        IEnumerable<Corps> corps)
    {
        services.AddSingleton<ICorpsService>(new CorpsService(corps));
        services.AddHttpClient<IRecapScraperTask, RecapScraperTask>();
        return services;
    }
}
