using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IAdminService adminService, IWebHostEnvironment env) : ControllerBase
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

    [HttpPost("corps/{id}/icon")]
    public async Task<IActionResult> UploadCorpsIcon(Guid id, IFormFile file)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp", "image/svg+xml" };

        if (!allowedTypes.Contains(file.ContentType))
        {
            return BadRequest(new { error = "File must be PNG, JPEG, WebP, or SVG." });
        }

        if (file.Length > 2 * 1024 * 1024)
        {
            return BadRequest(new { error = "File must be 2 MB or smaller." });
        }

        var ext = file.ContentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/webp" => "webp",
            "image/svg+xml" => "svg",
            _ => "png"
        };

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var relativePath = $"corps-icons/{id}-{uniqueSuffix}.{ext}";
        var uploadsDir = Path.Combine(env.ContentRootPath, "uploads", "corps-icons");
        Directory.CreateDirectory(uploadsDir);
        var filePath = Path.Combine(uploadsDir, $"{id}-{uniqueSuffix}.{ext}");

        await using (var stream = System.IO.File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        var (found, oldIconPath) = await adminService.SetCorpsIconAsync(id, relativePath);

        if (!found)
        {
            System.IO.File.Delete(filePath);

            return NotFound();
        }

        if (oldIconPath != null && oldIconPath != relativePath)
        {
            var oldFilePath = Path.Combine(env.ContentRootPath, "uploads", oldIconPath);

            if (System.IO.File.Exists(oldFilePath))
            {
                System.IO.File.Delete(oldFilePath);
            }
        }

        return Ok(new { iconUrl = $"/uploads/{relativePath}" });
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

    [HttpPut("seasons/{id}/corps/order")]
    public async Task<IActionResult> SetCorpsOrder(Guid id, SetCorpsOrderRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var orders = req.Orders.Select(o => (o.CorpsId, o.SortOrder)).ToList();
        var (found, canEdit) = await adminService.SetSeasonCorpsOrderAsync(id, orders);

        if (!found)
        {
            return NotFound();
        }

        if (!canEdit)
        {
            return Conflict(new { error = "Season is published and cannot be modified." });
        }

        return NoContent();
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

        try
        {
            var result = await adminService.CreateShowAsync(
                seasonId, req.Name, req.Url, req.Date, req.StartTime, req.ScoresAnnouncedTime,
                req.Timezone, req.IsExhibition, req.Location, req.Latitude, req.Longitude,
                req.CorpsIds, req.Schedule);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.UpdateShowAsync(
            id, req.Name, req.Url, req.Date, req.StartTime, req.ScoresAnnouncedTime,
            req.Timezone, req.IsExhibition, req.Location, req.Latitude, req.Longitude,
            req.CorpsIds, req.Schedule) ? NoContent() : NotFound();
    }

    [HttpGet("seasons/{seasonId}/shows/prefill")]
    public async Task<IActionResult> PrefillShow(Guid seasonId, [FromQuery] string name)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var result = await adminService.PrefillShowAsync(name, seasonId);

        if (result is null)
        {
            return NotFound(new { error = "Could not fetch show info from DCI. Check the show name and try again." });
        }

        return Ok(result);
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

        var (found, outcome, error) = await adminService.TriggerScrapeAsync(id);

        return found ? Ok(new { outcome, error }) : NotFound();
    }
}
