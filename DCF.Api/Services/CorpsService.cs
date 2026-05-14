using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class CorpsService(DcfDbContext db) : ICorpsService
{
    public async Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default)
    {
        var corps = await db.Corps.ToListAsync(ct);

        return corps.ToDictionary(c => c.Name, c => new Corps(c.Id, c.Name));
    }
}
