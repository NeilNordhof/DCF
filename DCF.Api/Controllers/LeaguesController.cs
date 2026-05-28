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

    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var leagues = await leagueService.GetPublicLeaguesAsync();

        return Ok(leagues);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { error = "code is required." });
        }

        var leagueId = await leagueService.LookupByCodeAsync(code);

        if (leagueId is null)
        {
            return NotFound(new { error = "No league found with that code." });
        }

        return Ok(new { leagueId });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeagueRequest req)
    {
        var userSub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

        try
        {
            var league = await leagueService.CreateAsync(
                req.Name, req.IsPublic, req.CorpsPerCaption, req.MaxPlayers,
                req.DraftableCaptions.ToList(), userSub, req.DraftStartTime);

            return CreatedAtAction(nameof(Get), new { id = league.Id }, new { id = league.Id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
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
            JoinResult.Full => Conflict(new { error = "This league is full." }),
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
    public async Task<IActionResult> Get(Guid id, [FromQuery] string? code)
    {
        var league = await leagueService.GetAsync(id, GetSub(), code);

        if (league is null)
        {
            return NotFound();
        }

        return Ok(league);
    }
}
