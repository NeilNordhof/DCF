using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score);

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
                    var latestScore = await GetEffectiveScoreAsync(pick.CorpsId, caption);

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

    /// <summary>
    /// Returns the effective fantasy score for a corps in the given caption.
    /// VisualPerformance is a virtual caption — no ScoreEntity rows are scraped for it;
    /// its score is derived by summing the corps' latest VisualAnalysis and VisualProficiency
    /// sub-scores from the same show.
    /// </summary>
    private async Task<double?> GetEffectiveScoreAsync(Guid corpsId, Caption caption)
    {
        if (caption == Caption.VisualPerformance)
        {
            var latestShowId = await db.Scores
                .Include(s => s.Show)
                .Where(s => s.CorpsId == corpsId &&
                            (s.Caption == Caption.VisualAnalysis || s.Caption == Caption.VisualProficiency))
                .GroupBy(s => s.ShowId)
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Max(s => s.Show.Date))
                .Select(g => (Guid?)g.Key)
                .FirstOrDefaultAsync();

            if (latestShowId is null)
            {
                return null;
            }

            var subScores = await db.Scores
                .Where(s => s.CorpsId == corpsId && s.ShowId == latestShowId.Value &&
                            (s.Caption == Caption.VisualAnalysis || s.Caption == Caption.VisualProficiency))
                .Select(s => s.TotalScore)
                .ToListAsync();

            if (subScores.Count < 2)
            {
                return null;
            }

            return subScores.Sum(x => (double)x);
        }

        return await db.Scores
            .Include(s => s.Show)
            .Where(s => s.CorpsId == corpsId && s.Caption == caption)
            .OrderByDescending(s => s.Show.Date)
            .Select(s => (double?)s.TotalScore)
            .FirstOrDefaultAsync();
    }
}
