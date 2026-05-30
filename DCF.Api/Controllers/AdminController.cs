using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IAdminService adminService) : ControllerBase
{
    private string GetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No sub claim");
    }

    // --- Seasons ---

    [HttpGet("seasons")]
    public async Task<IActionResult> GetSeasons()
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetSeasonsAsync());
    }

    [HttpGet("seasons/{id}")]
    public async Task<IActionResult> GetSeason(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var detail = await adminService.GetSeasonDetailAsync(id);

        if (detail is null)
        {
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason(CreateSeasonRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.CreateSeasonAsync(req.Year, req.StartDate, req.EndDate));
    }

    [HttpPost("seasons/{id}/publish")]
    public async Task<IActionResult> PublishSeason(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.PublishSeasonAsync(id) ? NoContent() : NotFound();
    }

    [HttpPatch("seasons/{id}/dates")]
    public async Task<IActionResult> UpdateSeasonDates(Guid id, UpdateSeasonDatesRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.UpdateSeasonDatesAsync(id, req.StartDate, req.EndDate)
            ? NoContent()
            : NotFound();
    }

    // --- Corps ---

    [HttpGet("corps")]
    public async Task<IActionResult> GetCorps()
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetCorpsAsync());
    }

    [HttpPost("corps")]
    public async Task<IActionResult> CreateCorps(CreateCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.CreateCorpsAsync(req.Name));
    }

    [HttpPatch("corps/{id}")]
    public async Task<IActionResult> RenameCorps(Guid id, RenameCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var result = await adminService.RenameCorpsAsync(id, req.Name);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpDelete("corps/{id}")]
    public async Task<IActionResult> DeleteCorps(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var (found, deletable) = await adminService.DeleteCorpsAsync(id);

        if (!found)
        {
            return NotFound();
        }

        if (!deletable)
        {
            return Conflict(new { error = "Corps belongs to a published season and cannot be deleted." });
        }

        return NoContent();
    }

    [HttpPut("seasons/{seasonId}/corps")]
    public async Task<IActionResult> SetSeasonCorps(Guid seasonId, SetSeasonCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.SetSeasonCorpsAsync(seasonId, req.CorpsIds) ? NoContent() : NotFound();
    }

    // --- Shows ---

    [HttpGet("seasons/{seasonId}/shows")]
    public async Task<IActionResult> GetShows(Guid seasonId)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetShowsAsync(seasonId));
    }

    [HttpPost("seasons/{seasonId}/shows")]
    public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var result = await adminService.CreateShowAsync(seasonId, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds);

        return Ok(result);
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.UpdateShowAsync(id, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds) ? NoContent() : NotFound();
    }

    [HttpDelete("shows/{id}")]
    public async Task<IActionResult> DeleteShow(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.DeleteShowAsync(id) ? NoContent() : NotFound();
    }

    // --- Manual scrape trigger ---

    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.TriggerScrapeAsync(id) ? Ok() : NotFound();
    }
}
