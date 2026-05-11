using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public interface ICorpsService
{
    Dictionary<string, Corps> GetCorps();
}
