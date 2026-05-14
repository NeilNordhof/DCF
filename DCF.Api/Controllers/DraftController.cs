using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues/{leagueId}/draft")]
[Authorize]
public class DraftController(DraftService draftService) : ControllerBase
{
    private string GetSub() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No sub claim");

    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid leagueId)
    {
        try
        {
            await draftService.StartDraftAsync(leagueId, GetSub());
            return Ok();
        }
        catch (ArgumentException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick(Guid leagueId, SubmitPickRequest req)
    {
        try
        {
            var (id, pickNumber) = await draftService.SubmitPickAsync(leagueId, GetSub(), req.CorpsId, req.Caption);
            return Ok(new { Id = id, PickNumber = pickNumber });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(Guid leagueId)
    {
        try
        {
            await draftService.SkipCurrentPickAsync(leagueId, GetSub());
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}
