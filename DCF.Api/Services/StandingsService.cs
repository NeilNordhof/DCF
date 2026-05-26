using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public record PickScore(string CorpsName, double? Score);

public record CaptionBreakdown(double Avg, List<PickScore> Picks);

public record MemberScoreBreakdown(Guid UserId, string DisplayName, double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public class StandingsService(DcfDbContext db) : IStandingsService
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        return members
            .Select(m => new MemberStanding(
                m.UserId, m.User.DisplayName, 0,
                new Dictionary<ComputedCaption, CaptionBreakdown>()))
            .ToList();
    }

    public async Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        return members
            .Select(m => new MemberScoreBreakdown(
                m.UserId, m.User.DisplayName, 0,
                new Dictionary<ComputedCaption, CaptionBreakdown>()))
            .ToList();
    }
}
