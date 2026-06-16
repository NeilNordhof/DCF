using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DCF.Api.Services;

public class DraftService(DcfDbContext db, IMqttService mqtt, IPresenceService presenceService) : IDraftService
{
    public static string GetCurrentDrafter(string[] draftOrder, int currentPickNumber)
    {
        int n = draftOrder.Length;
        int round = currentPickNumber / n;
        int positionInRound = currentPickNumber % n;
        int index = round % 2 == 0 ? positionInRound : n - 1 - positionInRound;

        return draftOrder[index];
    }

    public async Task OpenDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus == DraftStatus.Open)
        {
            return;
        }

        if (league.DraftStatus != DraftStatus.NotStarted && league.DraftStatus != DraftStatus.Scheduled)
        {
            throw new InvalidOperationException("Draft can only be opened from NotStarted or Scheduled status");
        }

        await OpenDraftCoreAsync(league);
    }

    public async Task OpenDraftAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can open the draft");
        }

        if (league.DraftStatus != DraftStatus.NotStarted)
        {
            throw new InvalidOperationException("Draft can only be opened from NotStarted status");
        }

        await OpenDraftCoreAsync(league);
    }

    private async Task OpenDraftCoreAsync(LeagueEntity league)
    {
        if (league.Members.Count < 4)
        {
            throw new InvalidOperationException("At least 4 players must join before the draft can open");
        }

        var shuffled = league.Members
            .Select(m => m.UserId.ToString())
            .ToArray();
        Random.Shared.Shuffle(shuffled);

        league.DraftOrderJson = JsonSerializer.Serialize(shuffled);
        league.DraftStatus = DraftStatus.Open;
        league.IssueMessages = [];

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    public async Task StartDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus != DraftStatus.Open)
        {
            throw new InvalidOperationException("Draft must be opened before starting");
        }

        await StartDraftCoreAsync(league);
    }

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

        if (league.DraftStatus != DraftStatus.Open)
        {
            throw new InvalidOperationException("Draft must be opened before starting");
        }

        await StartDraftCoreAsync(league);
    }

    private async Task StartDraftCoreAsync(LeagueEntity league)
    {
        league.CurrentPickNumber = 0;
        league.DraftStatus = DraftStatus.InProgress;

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    public async Task<(Guid Id, int PickNumber)> SubmitPickAsync(
        Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption)
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
        int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;
        var completedPickNumbers = new HashSet<int>(league.DraftPicks.Select(p => p.PickNumber));
        bool inMakeupPhase = league.CurrentPickNumber >= mainTotalPicks;

        List<string>? makeupQueue = null;

        if (inMakeupPhase)
        {
            makeupQueue = Enumerable
                .Range(0, mainTotalPicks)
                .Where(i => !completedPickNumbers.Contains(i))
                .Select(i => GetCurrentDrafter(draftOrder, i))
                .ToList();

            if (!makeupQueue.Contains(user.Id.ToString()))
            {
                throw new InvalidOperationException("You have no makeup picks remaining");
            }
        }
        else
        {
            var currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);

            if (currentDrafterId != user.Id.ToString())
            {
                throw new InvalidOperationException("Not your turn");
            }
        }

        var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
            p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);

        if (alreadyPicked)
        {
            throw new InvalidOperationException("That corps+caption is already drafted in this league");
        }

        var picksForCaption = league.DraftPicks.Count(p => p.UserId == user.Id && p.Caption == caption);

        if (picksForCaption >= league.CorpsPerCaption)
        {
            throw new InvalidOperationException($"You have already drafted the maximum {league.CorpsPerCaption} corps for this caption");
        }

        DraftPickEntity pick;

        if (!inMakeupPhase)
        {
            int round = league.CurrentPickNumber / draftOrder.Length;

            pick = new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
                CorpsId = corpsId, Caption = caption,
                PickNumber = league.CurrentPickNumber, RoundNumber = round
            };

            db.DraftPicks.Add(pick);

            league.CurrentPickNumber++;

            if (league.CurrentPickNumber >= mainTotalPicks)
            {
                completedPickNumbers.Add(pick.PickNumber);
                bool noMakeupPicks = !Enumerable.Range(0, mainTotalPicks).Any(i => !completedPickNumbers.Contains(i));

                if (noMakeupPicks)
                {
                    league.DraftStatus = DraftStatus.Completed;
                }
            }
        }
        else
        {
            int gapSlot = Enumerable
                .Range(0, mainTotalPicks)
                .First(i => !completedPickNumbers.Contains(i) && GetCurrentDrafter(draftOrder, i) == user.Id.ToString());

            pick = new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
                CorpsId = corpsId, Caption = caption,
                PickNumber = gapSlot, RoundNumber = gapSlot / draftOrder.Length
            };

            db.DraftPicks.Add(pick);

            completedPickNumbers.Add(gapSlot);
            bool noGapsRemain = !Enumerable.Range(0, mainTotalPicks).Any(i => !completedPickNumbers.Contains(i));

            if (noGapsRemain)
            {
                league.DraftStatus = DraftStatus.Completed;
            }
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
        int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;

        if (league.CurrentPickNumber >= mainTotalPicks)
        {
            throw new InvalidOperationException("Cannot skip during the makeup phase");
        }

        league.CurrentPickNumber++;

        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    public async Task PublishStateAsync(Guid leagueId)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league is null)
        {
            return;
        }

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

        int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;
        bool inMakeupPhase = draftOrder.Length > 0 && league.CurrentPickNumber >= mainTotalPicks;

        var completedPickNumbers = new HashSet<int>(picks.Select(p => p.PickNumber));
        var makeupQueue = Enumerable
            .Range(0, Math.Min(league.CurrentPickNumber, mainTotalPicks))
            .Where(i => !completedPickNumbers.Contains(i))
            .Select(i => GetCurrentDrafter(draftOrder, i))
            .ToList();

        string? currentDrafterId = null;

        if (league.DraftStatus == DraftStatus.InProgress && draftOrder.Length > 0 && !inMakeupPhase)
        {
            currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);
        }

        var membersByUserId = members.ToDictionary(m => m.UserId.ToString(), m => m.User.DisplayName);
        var draftOrderPayload = draftOrder
            .Where(membersByUserId.ContainsKey)
            .Select(id => new { UserId = id, DisplayName = membersByUserId[id] })
            .ToArray();

        var onlineUserIds = presenceService.GetOnline(league.Id)
            .Select(id => id.ToString())
            .ToArray();

        var payload = new
        {
            Status = league.DraftStatus.ToString(),
            league.DraftStartTime,
            league.CurrentPickNumber,
            MainTotalPicks = mainTotalPicks,
            MakeupQueue = makeupQueue,
            CurrentDrafterId = currentDrafterId,
            DraftOrder = draftOrderPayload,
            Members = members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = picks.Select(p => new
            {
                p.PickNumber, p.RoundNumber,
                UserId = p.UserId, p.User.DisplayName,
                CorpsId = p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString()
            }),
            OnlineUserIds = onlineUserIds
        };

        await mqtt.PublishAsync($"dcf/leagues/{league.Id}/draft", payload, retain: true);
    }
}
