using DCF.Data.Models;

namespace DCF.Api.Scraping;

public interface IRecapScraperTask
{
    Task<List<Result>> ScrapeAsync(Show show);
}
