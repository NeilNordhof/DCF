using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public record PickScore(string CorpsName, double? Score, string? IconUrl);

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

        var corpsList = await db.Corps
            .Select(c => new { c.Id, c.Name, c.IconPath })
            .ToListAsync();
        var corpsNames = corpsList.ToDictionary(c => c.Id, c => c.Name);
        var corpsIcons = corpsList
            .Where(c => c.IconPath != null)
            .ToDictionary(c => c.Id, c => $"/uploads/{c.IconPath!}");

        var latestByCorps = await LoadLatestComputedScoresAsync(league.SeasonId);

        var standings = new List<MemberStanding>();

        foreach (var member in members)
        {
            var (totalScore, captions) = await ComputeMemberScoreAsync(
                leagueId, member.UserId, league, latestByCorps, corpsNames, corpsIcons);

            standings.Add(new MemberStanding(member.UserId, member.User.DisplayName, totalScore, captions));
        }

        return standings.OrderByDescending(s => s.Score).ToList();
    }

    public async Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var corpsList = await db.Corps
            .Select(c => new { c.Id, c.Name, c.IconPath })
            .ToListAsync();
        var corpsNames = corpsList.ToDictionary(c => c.Id, c => c.Name);
        var corpsIcons = corpsList
            .Where(c => c.IconPath != null)
            .ToDictionary(c => c.Id, c => $"/uploads/{c.IconPath!}");

        var latestByCorps = await LoadLatestComputedScoresAsync(league.SeasonId);

        var result = new List<MemberScoreBreakdown>();

        foreach (var member in members)
        {
            var (totalScore, captions) = await ComputeMemberScoreAsync(
                leagueId, member.UserId, league, latestByCorps, corpsNames, corpsIcons);

            result.Add(new MemberScoreBreakdown(
                member.UserId, member.User.DisplayName, totalScore, captions));
        }

        return result.OrderByDescending(r => r.TotalScore).ToList();
    }

    public async Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId)
    {
        var standings = await GetStandingsAsync(leagueId);

        if (standings.Count == 0)
        {
            return (null, null);
        }

        var idx = standings.FindIndex(s => s.UserId == userId);

        if (idx < 0)
        {
            return (null, null);
        }

        return (idx + 1, standings[idx].Score);
    }

    private async Task<Dictionary<Guid, ComputedScoreEntity>> LoadLatestComputedScoresAsync(Guid seasonId)
    {
        var allSeasonScores = await db.ComputedScores
            .Include(cs => cs.Show)
            .Where(cs => cs.SeasonId == seasonId)
            .ToListAsync();

        return allSeasonScores
            .GroupBy(cs => cs.CorpsId)
            .ToDictionary(
                g => g.Key,
                g => g.MaxBy(cs => cs.Show.Date)!);
    }

    private async Task<(double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions)>
        ComputeMemberScoreAsync(
            Guid leagueId,
            Guid userId,
            LeagueEntity league,
            Dictionary<Guid, ComputedScoreEntity> latestByCorps,
            Dictionary<Guid, string> corpsNames,
            Dictionary<Guid, string> corpsIcons)
    {
        double totalScore = 0;
        var captions = new Dictionary<ComputedCaption, CaptionBreakdown>();

        foreach (var caption in league.DraftableCaptions)
        {
            var picks = await db.DraftPicks
                .Where(p => p.LeagueId == leagueId &&
                            p.UserId == userId &&
                            p.Caption == caption)
                .ToListAsync();

            var pickScores = new List<PickScore>();
            var captionScores = new List<double>();

            foreach (var pick in picks)
            {
                var corpsName = corpsNames.GetValueOrDefault(pick.CorpsId, "Unknown");
                corpsIcons.TryGetValue(pick.CorpsId, out var iconUrl);

                if (latestByCorps.TryGetValue(pick.CorpsId, out var cs))
                {
                    var score = GetCaptionValue(cs, caption);
                    pickScores.Add(new PickScore(corpsName, score, iconUrl));
                    captionScores.Add(score);
                }
                else
                {
                    pickScores.Add(new PickScore(corpsName, null, iconUrl));
                }
            }

            var avg = captionScores.Count > 0 ? captionScores.Average() : 0;
            var weight = GetWeight(caption, league.DraftableCaptions);
            var weighted = avg * weight;
            totalScore += weighted;

            captions[caption] = new CaptionBreakdown(avg, pickScores);
        }

        return (totalScore, captions);
    }

    private static double GetCaptionValue(ComputedScoreEntity cs, ComputedCaption caption)
    {
        return caption switch
        {
            ComputedCaption.GeneralEffectCombined => cs.GeneralEffectCombined,
            ComputedCaption.GeneralEffect1 => cs.GeneralEffect1,
            ComputedCaption.GeneralEffect2 => cs.GeneralEffect2,
            ComputedCaption.VisualCombined => cs.VisualCombined,
            ComputedCaption.Visual => cs.Visual,
            ComputedCaption.Colorguard => cs.Colorguard,
            ComputedCaption.VisualProficiency => cs.VisualProficiency,
            ComputedCaption.VisualAnalysis => cs.VisualAnalysis,
            ComputedCaption.MusicCombined => cs.MusicCombined,
            ComputedCaption.Brass => cs.Brass,
            ComputedCaption.Percussion => cs.Percussion,
            ComputedCaption.MusicAnalysis => cs.MusicAnalysis,
            _ => 0
        };
    }

    private static double GetWeight(ComputedCaption caption, ComputedCaption[] draftableCaptions)
    {
        if (caption is ComputedCaption.GeneralEffectCombined or
            ComputedCaption.GeneralEffect1 or ComputedCaption.GeneralEffect2)
        {
            return 1.0;
        }

        if (caption == ComputedCaption.VisualCombined)
        {
            return 1.0;
        }

        if (caption is ComputedCaption.Visual or ComputedCaption.VisualProficiency or
            ComputedCaption.VisualAnalysis or ComputedCaption.Colorguard)
        {
            return draftableCaptions.Contains(ComputedCaption.Visual) ? 0.75 : 0.5;
        }

        if (caption == ComputedCaption.MusicCombined)
        {
            return 1.0;
        }

        if (caption is ComputedCaption.Brass or ComputedCaption.Percussion or ComputedCaption.MusicAnalysis)
        {
            return draftableCaptions.Contains(ComputedCaption.MusicAnalysis) ? 0.5 : 0.75;
        }

        return 1.0;
    }
}
