using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record DciSeasonDto(Guid Id, int Year);

public class DciPublicService(DcfDbContext db) : IDciPublicService
{
    public async Task<DciSeasonDto?> GetCurrentSeasonAsync()
    {
        var active = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Active)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        if (active is not null)
        {
            return new DciSeasonDto(active.Id, active.Year);
        }

        var completed = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Completed)
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.EndDate)
            .FirstOrDefaultAsync();

        if (completed is not null)
        {
            return new DciSeasonDto(completed.Id, completed.Year);
        }

        var upcoming = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Upcoming)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        return upcoming is null ? null : new DciSeasonDto(upcoming.Id, upcoming.Year);
    }
}
