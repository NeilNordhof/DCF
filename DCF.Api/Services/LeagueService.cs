using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DCF.Api.Services;

public record LeagueSummary(
    Guid Id, string Name, bool IsPublic, DraftStatus DraftStatus,
    DateTimeOffset? DraftStartTime, Guid CommissionerUserId,
    int SeasonYear, bool IsMember, int MemberCount);

public record LeagueDetail(
    Guid Id, string Name, bool IsPublic, string InviteCode,
    DraftStatus DraftStatus, DateTimeOffset? DraftStartTime,
    int CorpsPerCaption, Guid CommissionerUserId,
    IEnumerable<string> DraftableCaptions, int SeasonYear,
    IEnumerable<MemberSummary> Members,
    IEnumerable<PickSummary> Picks);

public record MemberSummary(Guid UserId, string DisplayName);

public record PickSummary(
    Guid UserId, Guid CorpsId, string CorpsName,
    string Caption, int PickNumber, int RoundNumber);

public record LeagueBrief(Guid Id, string Name, string InviteCode);

public class LeagueService(DcfDbContext db, DraftSchedulerService draftScheduler)
{
    public async Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub);
        if (user is null) return [];

        var myLeagueIds = await db.LeagueMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.LeagueId)
            .ToListAsync();

        return await db.Leagues
            .Where(l => l.IsPublic || myLeagueIds.Contains(l.Id))
            .Select(l => new LeagueSummary(
                l.Id, l.Name, l.IsPublic, l.DraftStatus, l.DraftStartTime,
                l.CommissionerUserId, l.Season.Year,
                myLeagueIds.Contains(l.Id), l.Members.Count))
            .ToListAsync();
    }

    public async Task<LeagueBrief?> CreateAsync(string userSub, string name, bool isPublic,
        int corpsPerCaption, Caption[] draftableCaptions, DateTimeOffset? draftStartTime)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub);
        if (user is null) return null;

        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSeason is null) throw new InvalidOperationException("No active season");

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            SeasonId = activeSeason.Id,
            CommissionerUserId = user.Id,
            IsPublic = isPublic,
            InviteCode = GenerateInviteCode(),
            CorpsPerCaption = corpsPerCaption,
            DraftableCaptions = draftableCaptions,
            DraftStatus = draftStartTime.HasValue ? DraftStatus.Scheduled : DraftStatus.NotStarted,
            DraftStartTime = draftStartTime
        };
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        if (draftStartTime.HasValue)
            draftScheduler.ScheduleDraftStart(league.Id, draftStartTime.Value);

        return new LeagueBrief(league.Id, league.Name, league.InviteCode);
    }

    public async Task<JoinResult> JoinAsync(Guid leagueId, string userSub, string? inviteCode)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub);
        if (user is null) return JoinResult.Unauthorized;

        var league = await db.Leagues.FindAsync(leagueId);
        if (league is null) return JoinResult.NotFound;

        if (!league.IsPublic)
        {
            if (inviteCode is null ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(inviteCode),
                    System.Text.Encoding.UTF8.GetBytes(league.InviteCode)))
                return JoinResult.BadInviteCode;
        }

        var already = await db.LeagueMembers.AnyAsync(m => m.LeagueId == leagueId && m.UserId == user.Id);
        if (!already)
        {
            db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = leagueId, UserId = user.Id });
            await db.SaveChangesAsync();
        }

        return JoinResult.Ok;
    }

    public async Task<LeagueDetail?> GetAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members).ThenInclude(m => m.User)
            .Include(l => l.DraftPicks).ThenInclude(p => p.Corps)
            .Include(l => l.Season)
            .FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league is null) return null;

        return new LeagueDetail(
            league.Id, league.Name, league.IsPublic, league.InviteCode,
            league.DraftStatus, league.DraftStartTime, league.CorpsPerCaption,
            league.CommissionerUserId,
            league.DraftableCaptions.Select(c => c.ToString()),
            league.Season.Year,
            league.Members.Select(m => new MemberSummary(m.UserId, m.User.DisplayName)),
            league.DraftPicks.Select(p => new PickSummary(
                p.UserId, p.CorpsId, p.Corps.Name,
                p.Caption.ToString(), p.PickNumber, p.RoundNumber)));
    }

    private static string GenerateInviteCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        return Convert.ToBase64String(bytes)
            .Replace("+", "A").Replace("/", "B").Replace("=", "")[..8]
            .ToUpper();
    }
}

public enum JoinResult { Ok, Unauthorized, NotFound, BadInviteCode }
