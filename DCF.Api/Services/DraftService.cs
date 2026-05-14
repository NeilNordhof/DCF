using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DCF.Api.Services;

public class DraftService(DcfDbContext db, IMqttPublisherService mqtt)
{
    public static string GetCurrentDrafter(string[] draftOrder, int currentPickNumber)
    {
        int n = draftOrder.Length;
        int round = currentPickNumber / n;
        int positionInRound = currentPickNumber % n;
        int index = round % 2 == 0 ? positionInRound : n - 1 - positionInRound;

        return draftOrder[index];
    }

    // Used by DraftSchedulerService — no commissioner check
    public async Task StartDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        await StartDraftCoreAsync(league);
    }

    // Used by DraftController — validates calling user is the commissioner
    public async Task StartDraftAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can start the draft");
        }

        if (league.DraftStatus != DraftStatus.NotStarted && league.DraftStatus != DraftStatus.Scheduled)
        {
            throw new InvalidOperationException("Draft is already started or completed");
        }

        await StartDraftCoreAsync(league);
    }

    private async Task StartDraftCoreAsync(LeagueEntity league)
    {
        var shuffled = league.Members
            .Select(m => m.UserId.ToString())
            .ToArray();
        Random.Shared.Shuffle(shuffled);

        league.DraftOrderJson = JsonSerializer.Serialize(shuffled);
        league.CurrentPickNumber = 0;
        league.DraftStatus = DraftStatus.InProgress;

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    public async Task<(Guid Id, int PickNumber)> SubmitPickAsync(
        Guid leagueId, string userSub, Guid corpsId, Caption caption)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .Include(l => l.DraftPicks)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus != DraftStatus.InProgress)
        {
            throw new InvalidOperationException("Draft is not in progress");
        }

        var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson)!;
        var currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);

        if (currentDrafterId != user.Id.ToString())
        {
            throw new InvalidOperationException("Not your turn");
        }

        var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
            p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);

        if (alreadyPicked)
        {
            throw new InvalidOperationException("That corps+caption is already drafted in this league");
        }

        int totalPicks = league.Members.Count * league.DraftableCaptions.Length * league.CorpsPerCaption;
        int round = league.CurrentPickNumber / draftOrder.Length;
        var pick = new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
            CorpsId = corpsId, Caption = caption,
            PickNumber = league.CurrentPickNumber, RoundNumber = round
        };
        db.DraftPicks.Add(pick);

        league.CurrentPickNumber++;

        if (league.CurrentPickNumber >= totalPicks)
        {
            league.DraftStatus = DraftStatus.Completed;
        }

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);

        return (pick.Id, pick.PickNumber);
    }

    public async Task SkipCurrentPickAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can skip picks");
        }

        if (league.DraftStatus != DraftStatus.InProgress)
        {
            throw new InvalidOperationException("Draft is not in progress");
        }

        var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson)!;
        int totalPicks = league.Members.Count * league.DraftableCaptions.Length * league.CorpsPerCaption;

        league.CurrentPickNumber++;

        if (league.CurrentPickNumber >= totalPicks)
        {
            league.DraftStatus = DraftStatus.Completed;
        }

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    private async Task PublishDraftStateAsync(LeagueEntity league)
    {
        var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson) ?? [];

        var picks = await db.DraftPicks
            .Include(p => p.Corps)
            .Include(p => p.User)
            .Where(p => p.LeagueId == league.Id)
            .OrderBy(p => p.PickNumber)
            .ToListAsync();

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == league.Id)
            .ToListAsync();

        string? currentDrafterId = league.DraftStatus == DraftStatus.InProgress && draftOrder.Length > 0
            ? GetCurrentDrafter(draftOrder, league.CurrentPickNumber)
            : null;

        var payload = new
        {
            Status = league.DraftStatus.ToString(),
            league.DraftStartTime,
            league.CurrentPickNumber,
            CurrentDrafterId = currentDrafterId,
            Members = members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = picks.Select(p => new
            {
                p.PickNumber, p.RoundNumber,
                UserId = p.UserId, p.User.DisplayName,
                CorpsId = p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString()
            })
        };

        await mqtt.PublishAsync($"dcf/leagues/{league.Id}/draft", payload);
    }
}
