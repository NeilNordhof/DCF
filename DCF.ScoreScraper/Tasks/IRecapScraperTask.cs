using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Tasks;

public interface IRecapScraperTask
{
    Task<List<Result>> ScrapeAsync(Show show);
}
