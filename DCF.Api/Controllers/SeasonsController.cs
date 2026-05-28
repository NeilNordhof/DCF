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
}
