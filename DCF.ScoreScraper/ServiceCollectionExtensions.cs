using DCF.ScoreScraper.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace DCF.ScoreScraper
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            return services.AddSingleton<ICorpsSevice, CorpsSevice>();
        }
    }
}
