using DCF.Data.Models;

namespace DCF.Api.Services;

public interface ICorpsService
{
    Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default);
}
