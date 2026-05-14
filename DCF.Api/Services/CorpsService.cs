using DCF.Data.Models;

namespace DCF.Api.Services;

public class CorpsService(Func<CancellationToken, Task<IReadOnlyDictionary<string, Corps>>> factory) : ICorpsService
{
    public Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default)
    {
        return factory(ct);
    }
}
