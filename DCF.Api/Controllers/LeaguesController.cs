using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues")]
[Authorize]
public class LeaguesController(
    DcfDbContext db,
    DraftSchedulerService draftScheduler,
    StandingsService standingsService) : ControllerBase
{
    private async Task<UserEntity?> GetUserAsync() =>
        await db.Users.FirstOrDefaultAsync(u =>
            u.Auth0Sub == (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")));

    [HttpGet]
    public async Task<IActionResult> Browse()
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var myLeagueIds = await db.LeagueMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.LeagueId)
            .ToListAsync();

        var leagues = await db.Leagues
            .Where(l => l.IsPublic || myLeagueIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id, l.Name, l.IsPublic, l.DraftStatus, l.DraftStartTime,
                l.CommissionerUserId,
                SeasonYear = l.Season.Year, IsMember = myLeagueIds.Contains(l.Id),
                MemberCount = l.Members.Count
            })
            .ToListAsync();

        return Ok(leagues);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateLeagueRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSeason is null) return BadRequest("No active season");

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            SeasonId = activeSeason.Id,
            CommissionerUserId = user.Id,
            IsPublic = req.IsPublic,
            InviteCode = GenerateInviteCode(),
            CorpsPerCaption = req.CorpsPerCaption,
            DraftableCaptions = req.DraftableCaptions,
            DraftStatus = req.DraftStartTime.HasValue ? DraftStatus.Scheduled : DraftStatus.NotStarted,
            DraftStartTime = req.DraftStartTime
        };
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        if (req.DraftStartTime.HasValue)
            draftScheduler.ScheduleDraftStart(league.Id, req.DraftStartTime.Value);

        return Ok(new { league.Id, league.Name, league.InviteCode });
    }

    [HttpPost("{id}/join")]
    public async Task<IActionResult> Join(Guid id, JoinLeagueRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var league = await db.Leagues.FindAsync(id);
        if (league is null) return NotFound();

        if (!league.IsPublic)
        {
            if (req.InviteCode is null ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(req.InviteCode),
                    System.Text.Encoding.UTF8.GetBytes(league.InviteCode)))
                return BadRequest("Invalid invite code");
        }

        var already = await db.LeagueMembers.AnyAsync(m => m.LeagueId == id && m.UserId == user.Id);
        if (!already)
        {
            db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = id, UserId = user.Id });
            await db.SaveChangesAsync();
        }

        return Ok();
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

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var league = await db.Leagues
            .Include(l => l.Members).ThenInclude(m => m.User)
            .Include(l => l.DraftPicks).ThenInclude(p => p.Corps)
            .Include(l => l.Season)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (league is null) return NotFound();

        return Ok(new
        {
            league.Id, league.Name, league.IsPublic, league.InviteCode,
            league.DraftStatus, league.DraftStartTime, league.CorpsPerCaption,
            league.CommissionerUserId,
            DraftableCaptions = league.DraftableCaptions.Select(c => c.ToString()),
            SeasonYear = league.Season.Year,
            Members = league.Members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = league.DraftPicks.Select(p => new
            {
                p.UserId, p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString(), p.PickNumber, p.RoundNumber
            })
        });
    }

    private static string GenerateInviteCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        return Convert.ToBase64String(bytes)
            .Replace("+", "A").Replace("/", "B").Replace("=", "")[..8]
            .ToUpper();
    }
}
