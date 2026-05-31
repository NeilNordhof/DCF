using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/seasons")]
[Authorize]
public class SeasonsController(DcfDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var season = await db.Seasons
            .Include(s => s.SeasonCorps)
            .Where(s => s.IsPublished)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        if (season is null)
        {
            return NotFound();
        }

        return Ok(new { id = season.Id, year = season.Year, corpsCount = season.SeasonCorps.Count });
    }

    [HttpGet("{id}/corps")]
    public async Task<IActionResult> GetCorps(Guid id)
    {
        var exists = await db.Seasons.AnyAsync(s => s.Id == id);

        if (!exists)
        {
            return NotFound();
        }

        var corps = await db.SeasonCorps
            .Where(sc => sc.SeasonId == id)
            .Include(sc => sc.Corps)
            .OrderBy(sc => sc.SortOrder == null)
            .ThenBy(sc => sc.SortOrder)
            .ThenBy(sc => sc.Corps.Name)
            .Select(sc => new
            {
                id = sc.CorpsId,
                name = sc.Corps.Name,
                iconUrl = sc.Corps.IconPath != null ? "/uploads/" + sc.Corps.IconPath : null,
                sortOrder = sc.SortOrder
            })
            .ToListAsync();

        return Ok(corps);
    }
}
