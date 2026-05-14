using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public interface ICorpsService
{
    Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default);
}
