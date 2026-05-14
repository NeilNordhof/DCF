using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(AdminService adminService) : ControllerBase
{
    private string GetSub() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No sub claim");

    // --- Seasons ---

    [HttpGet("seasons")]
    public async Task<IActionResult> GetSeasons()
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return Ok(await adminService.GetSeasonsAsync());
    }

    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason(CreateSeasonRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return Ok(await adminService.CreateSeasonAsync(req.Year));
    }

    [HttpPut("seasons/{id}/activate")]
    public async Task<IActionResult> ActivateSeason(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return await adminService.ActivateSeasonAsync(id) ? NoContent() : NotFound();
    }

    // --- Corps ---

    [HttpGet("corps")]
    public async Task<IActionResult> GetCorps()
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return Ok(await adminService.GetCorpsAsync());
    }

    [HttpPost("corps")]
    public async Task<IActionResult> CreateCorps(CreateCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return Ok(await adminService.CreateCorpsAsync(req.Name));
    }

    [HttpPut("seasons/{seasonId}/corps")]
    public async Task<IActionResult> SetSeasonCorps(Guid seasonId, SetSeasonCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return await adminService.SetSeasonCorpsAsync(seasonId, req.CorpsIds) ? NoContent() : NotFound();
    }

    // --- Shows ---

    [HttpGet("seasons/{seasonId}/shows")]
    public async Task<IActionResult> GetShows(Guid seasonId)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return Ok(await adminService.GetShowsAsync(seasonId));
    }

    [HttpPost("seasons/{seasonId}/shows")]
    public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        var result = await adminService.CreateShowAsync(seasonId, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds);
        return Ok(result);
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return await adminService.UpdateShowAsync(id, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds) ? NoContent() : NotFound();
    }

    // --- Manual scrape trigger ---

    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub())) return Forbid();
        return await adminService.TriggerScrapeAsync(id) ? Ok() : NotFound();
    }
}
