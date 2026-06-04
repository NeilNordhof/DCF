using DCF.Api.Models;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DCF.Api.Services;

public record LeagueSummary(
    Guid Id, string Name, bool IsPublic, DraftStatus DraftStatus,
    DateTimeOffset? DraftStartTime, Guid CommissionerUserId,
    int SeasonYear, bool IsMember, int MemberCount, int MaxPlayers,
    int? UserRank, double? UserScore);

public record LeagueDetail(
    Guid Id, string Name, bool IsPublic, string? InviteCode,
    DraftStatus DraftStatus, DateTimeOffset? DraftStartTime,
    int CorpsPerCaption, Guid CommissionerUserId,
    IEnumerable<string> DraftableCaptions, int SeasonYear, Guid SeasonId,
    IEnumerable<MemberSummary> Members,
    IEnumerable<PickSummary> Picks,
    bool IsMember, bool IsCommissioner, int MaxPlayers);

public record MemberSummary(Guid UserId, string DisplayName);

public record PickSummary(
    Guid UserId, Guid CorpsId, string CorpsName,
    string Caption, int PickNumber, int RoundNumber);

public record LeagueBrief(Guid Id, string Name, string InviteCode);

public record PublicLeagueSummary(
    Guid Id,
    string Name,
    DraftStatus DraftStatus,
    int MemberCount,
    int MaxPlayers
);

public class LeagueService(DcfDbContext db, DraftSchedulerService draftScheduler, IStandingsService standingsService) : ILeagueService
{
    public async Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub);

        if (user is null)
        {
            return [];
        }

        var leagues = await db.LeagueMembers
            .Where(m => m.UserId == user.Id)
            .Include(m => m.League).ThenInclude(l => l.Season)
            .Include(m => m.League).ThenInclude(l => l.Members)
            .Select(m => m.League)
            .ToListAsync();

        var summaries = new List<LeagueSummary>();

        foreach (var league in leagues)
        {
            var (rank, score) = await standingsService.GetUserRankAsync(league.Id, user.Id);

            summaries.Add(new LeagueSummary(
                league.Id, league.Name, league.IsPublic, league.DraftStatus,
                league.DraftStartTime, league.CommissionerUserId,
                league.Season.Year, IsMember: true, league.Members.Count,
                league.MaxPlayers, rank, score));
        }

        return summaries;
    }

    public async Task<LeagueEntity> CreateAsync(
        string name,
        bool isPublic,
        int corpsPerCaption,
        int maxPlayers,
        List<ComputedCaption> captions,
        string userSub,
        DateTimeOffset? draftStartTime = null)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new InvalidOperationException("User not found.");

        var activeSeason = await db.Seasons
            .Include(s => s.SeasonCorps)
            .Where(s => s.IsPublished)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("No active season found.");

        var corpsCount = activeSeason.SeasonCorps.Count;
        var maxCorpsPerCaption = corpsCount / 4;
        var maxAllowedPlayers = corpsPerCaption > 0 ? corpsCount / corpsPerCaption : 0;

        if (maxPlayers < 4)
        {
            throw new ArgumentException("maxPlayers must be at least 4.", nameof(maxPlayers));
        }

        if (corpsPerCaption > maxCorpsPerCaption)
        {
            throw new ArgumentException(
                $"corpsPerCaption cannot exceed {maxCorpsPerCaption} for the active season.", nameof(corpsPerCaption));
        }

        if (maxPlayers > maxAllowedPlayers)
        {
            throw new ArgumentException(
                $"maxPlayers cannot exceed {maxAllowedPlayers} for the given corpsPerCaption.", nameof(maxPlayers));
        }

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            SeasonId = activeSeason.Id,
            CommissionerUserId = user.Id,
            IsPublic = isPublic,
            InviteCode = GenerateInviteCode(),
            MaxPlayers = maxPlayers,
            CorpsPerCaption = corpsPerCaption,
            DraftableCaptions = captions.ToArray(),
            DraftStatus = draftStartTime.HasValue ? DraftStatus.Scheduled : DraftStatus.NotStarted,
            DraftStartTime = draftStartTime?.ToUniversalTime()
        };
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });

        await db.SaveChangesAsync();

        if (draftStartTime.HasValue)
        {
            draftScheduler.ScheduleNext(league.Id, draftStartTime.Value, isAlreadyOpened: false);
        }

        return league;
    }

    public async Task<JoinResult> JoinAsync(Guid leagueId, string userSub, string? inviteCode)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub);

        if (user is null)
        {
            return JoinResult.Unauthorized;
        }

        var league = await db.Leagues.FindAsync(leagueId);

        if (league is null)
        {
            return JoinResult.NotFound;
        }

        if (!league.IsPublic)
        {
            if (inviteCode is null ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(inviteCode),
                    System.Text.Encoding.UTF8.GetBytes(league.InviteCode)))
            {
                return JoinResult.BadInviteCode;
            }
        }

        var memberCount = await db.LeagueMembers.CountAsync(m => m.LeagueId == leagueId);

        if (memberCount >= league.MaxPlayers)
        {
            return JoinResult.Full;
        }

        var already = await db.LeagueMembers.AnyAsync(m => m.LeagueId == leagueId && m.UserId == user.Id);

        if (!already)
        {
            db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = leagueId, UserId = user.Id });

            await db.SaveChangesAsync();
        }

        return JoinResult.Ok;
    }

    public async Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode)
    {
        var league = await db.Leagues
            .Include(l => l.Season)
            .Include(l => l.Members).ThenInclude(m => m.User)
            .Include(l => l.DraftPicks).ThenInclude(p => p.Corps)
            .FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league is null)
        {
            return null;
        }

        var user = userSub is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            : null;

        var isMember = user is not null && league.Members.Any(m => m.UserId == user.Id);
        var isCommissioner = user is not null && league.CommissionerUserId == user.Id;

        if (!isMember && !league.IsPublic)
        {
            if (inviteCode is null || string.IsNullOrEmpty(league.InviteCode))
            {
                return null;
            }

            var codeBytes = System.Text.Encoding.UTF8.GetBytes(inviteCode);
            var storedBytes = System.Text.Encoding.UTF8.GetBytes(league.InviteCode);

            if (!CryptographicOperations.FixedTimeEquals(codeBytes, storedBytes))
            {
                return null;
            }
        }

        return new LeagueDetail(
            league.Id, league.Name, league.IsPublic,
            isMember ? league.InviteCode : null,
            league.DraftStatus, league.DraftStartTime, league.CorpsPerCaption,
            league.CommissionerUserId,
            league.DraftableCaptions.Select(c => c.ToString()),
            league.Season.Year,
            league.SeasonId,
            league.Members.Select(m => new MemberSummary(m.UserId, m.User.DisplayName)),
            league.DraftPicks.Select(p => new PickSummary(
                p.UserId, p.CorpsId, p.Corps.Name,
                p.Caption.ToString(), p.PickNumber, p.RoundNumber)),
            isMember,
            isCommissioner,
            league.MaxPlayers);
    }

    public async Task<IReadOnlyList<PublicLeagueSummary>> GetPublicLeaguesAsync()
    {
        return await db.Leagues
            .Where(l => l.IsPublic)
            .Include(l => l.Members)
            .Select(l => new PublicLeagueSummary(
                l.Id,
                l.Name,
                l.DraftStatus,
                l.Members.Count,
                l.MaxPlayers
            ))
            .ToListAsync();
    }

    public async Task<Guid?> LookupByCodeAsync(string code)
    {
        return await db.Leagues
            .Where(l => l.InviteCode == code)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateAsync(Guid leagueId, UpdateLeagueRequest req, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Season).ThenInclude(s => s.SeasonCorps)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can update league settings");
        }

        if (league.DraftStatus != DraftStatus.NotStarted && league.DraftStatus != DraftStatus.Scheduled)
        {
            throw new InvalidOperationException("Draft settings can only be changed before the draft opens");
        }

        var corpsCount = league.Season.SeasonCorps.Count;
        var maxCorpsPerCaption = corpsCount / 4;
        var maxAllowedPlayers = req.CorpsPerCaption > 0 ? corpsCount / req.CorpsPerCaption : 0;

        if (req.CorpsPerCaption > maxCorpsPerCaption)
        {
            throw new ArgumentException($"corpsPerCaption cannot exceed {maxCorpsPerCaption} for the active season");
        }

        if (league.MaxPlayers > maxAllowedPlayers)
        {
            throw new ArgumentException($"corpsPerCaption {req.CorpsPerCaption} would require maxPlayers ≤ {maxAllowedPlayers}");
        }

        if (req.DraftableCaptions.Length < 3)
        {
            throw new ArgumentException("At least three captions are required");
        }

        league.CorpsPerCaption = req.CorpsPerCaption;
        league.DraftableCaptions = req.DraftableCaptions;

        if (req.DraftStartTime.HasValue)
        {
            if (req.DraftStartTime.Value < DateTimeOffset.UtcNow)
            {
                throw new ArgumentException("Draft Start date and time can not be in the past");
            }

            var wasScheduled = league.DraftStartTime.HasValue;

            league.DraftStartTime = req.DraftStartTime.Value.ToUniversalTime();
            league.DraftStatus = DraftStatus.Scheduled;

            if (wasScheduled)
            {
                draftScheduler.CancelScheduled(league.Id);
            }

            draftScheduler.ScheduleNext(league.Id, req.DraftStartTime.Value, isAlreadyOpened: false);
        }
        else
        {
            if (league.DraftStartTime.HasValue)
            {
                draftScheduler.CancelScheduled(league.Id);
            }

            league.DraftStartTime = null;
            league.DraftStatus = DraftStatus.NotStarted;
        }

        await db.SaveChangesAsync();
    }

    public async Task<string> RefreshInviteCodeAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can refresh the invite code");
        }

        league.InviteCode = GenerateInviteCode();

        await db.SaveChangesAsync();

        return league.InviteCode;
    }

    private static string GenerateInviteCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);

        return Convert.ToBase64String(bytes)
            .Replace("+", "A").Replace("/", "B").Replace("=", "")[..8]
            .ToUpper();
    }
}

public enum JoinResult { Ok, Unauthorized, NotFound, BadInviteCode, Full }
