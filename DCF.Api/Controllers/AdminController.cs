using System.Text.Json;
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttPublisherService mqtt) : ControllerBase
{
    private async Task<bool> IsAdminAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return await db.Users.AnyAsync(u => u.Auth0Sub == sub && u.IsAdmin);
    }

    // --- Seasons ---

    [HttpGet("seasons")]
    public async Task<IActionResult> GetSeasons()
    {
        if (!await IsAdminAsync()) return Forbid();
        var seasons = await db.Seasons.OrderByDescending(s => s.Year).ToListAsync();
        return Ok(seasons.Select(s => new { s.Id, s.Year, s.IsActive }));
    }

    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason(CreateSeasonRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = req.Year };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        return Ok(new { season.Id, season.Year, season.IsActive });
    }

    [HttpPut("seasons/{id}/activate")]
    public async Task<IActionResult> ActivateSeason(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (!await db.Seasons.AnyAsync(s => s.Id == id)) return NotFound();
        await db.Seasons.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await db.Seasons.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        return NoContent();
    }

    // --- Corps ---

    [HttpGet("corps")]
    public async Task<IActionResult> GetCorps()
    {
        if (!await IsAdminAsync()) return Forbid();
        var corps = await db.Corps.OrderBy(c => c.Name).ToListAsync();
        return Ok(corps.Select(c => new { c.Id, c.Name }));
    }

    [HttpPost("corps")]
    public async Task<IActionResult> CreateCorps(CreateCorpsRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = req.Name };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();
        return Ok(new { corps.Id, corps.Name });
    }

    [HttpPut("seasons/{seasonId}/corps")]
    public async Task<IActionResult> SetSeasonCorps(Guid seasonId, SetSeasonCorpsRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (!await db.Seasons.AnyAsync(s => s.Id == seasonId)) return NotFound();
        var existing = await db.SeasonCorps.Where(sc => sc.SeasonId == seasonId).ToListAsync();
        db.SeasonCorps.RemoveRange(existing);
        db.SeasonCorps.AddRange(req.CorpsIds.Select(cId =>
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = cId }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    // --- Shows ---

    [HttpGet("seasons/{seasonId}/shows")]
    public async Task<IActionResult> GetShows(Guid seasonId)
    {
        if (!await IsAdminAsync()) return Forbid();
        var shows = await db.Shows
            .Where(s => s.SeasonId == seasonId)
            .Include(s => s.ShowCorps)
            .OrderBy(s => s.Date)
            .ToListAsync();
        return Ok(shows.Select(s => new
        {
            s.Id, s.Name, s.Url, s.Date, s.ScoresAnnouncedTime,
            CorpsIds = s.ShowCorps.Select(sc => sc.CorpsId)
        }));
    }

    [HttpPost("seasons/{seasonId}/shows")]
    public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = req.Name, Url = req.Url,
            Date = req.Date, ScoresAnnouncedTime = req.ScoresAnnouncedTime, SeasonId = seasonId
        };
        db.Shows.Add(show);
        db.ShowCorps.AddRange(req.CorpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));
        await db.SaveChangesAsync();
        scrapeScheduler.ScheduleScrape(show);
        return Ok(new { show.Id, show.Name });
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = await db.Shows.FindAsync(id);
        if (show is null) return NotFound();
        show.Name = req.Name; show.Url = req.Url;
        show.Date = req.Date; show.ScoresAnnouncedTime = req.ScoresAnnouncedTime;
        var existing = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(existing);
        db.ShowCorps.AddRange(req.CorpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = id, CorpsId = cId }));
        await db.SaveChangesAsync();
        var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
        scrapeScheduler.ScheduleScrape(updatedShow);
        return NoContent();
    }

    // --- Manual scrape trigger ---

    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == id);
        if (show is null) return NotFound();
        await scrapeScheduler.ExecuteScrapeAsync(show);
        await mqtt.PublishAsync("dcf/scores/updated", JsonSerializer.Serialize(new { ShowId = id }));
        return Ok();
    }
}
