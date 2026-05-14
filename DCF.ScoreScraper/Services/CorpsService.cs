using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public class CorpsService(Func<CancellationToken, Task<IReadOnlyDictionary<string, Corps>>> factory) : ICorpsService
{
    public Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default)
        => factory(ct);
}
