using DCF.Data;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score);

public class StandingsService(DcfDbContext db)
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var standings = new List<MemberStanding>();

        foreach (var member in members)
        {
            double totalScore = 0;

            foreach (var caption in league.DraftableCaptions)
            {
                var picks = await db.DraftPicks
                    .Where(p => p.LeagueId == leagueId &&
                                p.UserId == member.UserId &&
                                p.Caption == caption)
                    .ToListAsync();

                var captionScores = new List<double>();

                foreach (var pick in picks)
                {
                    var latestScore = await db.Scores
                        .Include(s => s.Show)
                        .Where(s => s.CorpsId == pick.CorpsId && s.Caption == caption)
                        .OrderByDescending(s => s.Show.Date)
                        .Select(s => (double?)s.TotalScore)
                        .FirstOrDefaultAsync();

                    if (latestScore.HasValue)
                    {
                        captionScores.Add(latestScore.Value);
                    }
                }

                if (captionScores.Count > 0)
                {
                    totalScore += captionScores.Average();
                }
            }

            standings.Add(new MemberStanding(member.UserId, member.User.DisplayName, totalScore));
        }

        return standings.OrderByDescending(s => s.Score).ToList();

    }
}
