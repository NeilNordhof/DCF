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
[Route("api/leagues/{leagueId}/draft")]
[Authorize]
public class DraftController(DcfDbContext db, DraftService draftService) : ControllerBase
{
    private async Task<UserEntity?> GetUserAsync() =>
        await db.Users.FirstOrDefaultAsync(u =>
            u.Auth0Sub == (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")));

    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid leagueId)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var league = await db.Leagues.FindAsync(leagueId);
        if (league is null) return NotFound();
        if (league.CommissionerUserId != user.Id) return Forbid();
        if (league.DraftStatus != DraftStatus.NotStarted && league.DraftStatus != DraftStatus.Scheduled)
            return BadRequest("Draft is already started or completed");

        await draftService.StartDraftAsync(leagueId);
        return Ok();
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick(Guid leagueId, SubmitPickRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        try
        {
            var pick = await draftService.SubmitPickAsync(leagueId, user.Id, req.CorpsId, req.Caption);
            return Ok(new { pick.Id, pick.PickNumber });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(Guid leagueId)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        try
        {
            await draftService.SkipCurrentPickAsync(leagueId, user.Id);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
