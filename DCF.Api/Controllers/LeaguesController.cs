using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues")]
[Authorize]
public class LeaguesController(ILeagueService leagueService, IStandingsService standingsService) : ControllerBase
{
    private string GetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No sub claim");
    }

    [HttpGet]
    public async Task<IActionResult> Browse()
    {
        var leagues = await leagueService.BrowseAsync(GetSub());

        return Ok(leagues);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateLeagueRequest req)
    {
        try
        {
            var result = await leagueService.CreateAsync(
                GetSub(), req.Name, req.IsPublic,
                req.CorpsPerCaption, req.DraftableCaptions, req.DraftStartTime);

            if (result is null)
            {
                return Unauthorized();
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/join")]
    public async Task<IActionResult> Join(Guid id, JoinLeagueRequest req)
    {
        var result = await leagueService.JoinAsync(id, GetSub(), req.InviteCode);

        return result switch
        {
            JoinResult.Ok => NoContent(),
            JoinResult.Unauthorized => Unauthorized(),
            JoinResult.NotFound => NotFound(),
            JoinResult.BadInviteCode => BadRequest("Invalid invite code"),
            _ => StatusCode(500)
        };
    }

    [HttpGet("{id}/standings")]
    public async Task<IActionResult> Standings(Guid id)
    {
        try
        {
            var standings = await standingsService.GetStandingsAsync(id);

            return Ok(standings);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id}/standings/breakdown")]
    public async Task<IActionResult> StandingsBreakdown(Guid id)
    {
        try
        {
            var breakdown = await standingsService.GetScoreBreakdownAsync(id);

            return Ok(breakdown);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var league = await leagueService.GetAsync(id);

        if (league is null)
        {
            return NotFound();
        }

        return Ok(league);
    }
}
