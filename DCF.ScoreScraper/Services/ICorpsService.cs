using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public interface ICorpsService
{
    IReadOnlyDictionary<string, Corps> GetCorps();
}
