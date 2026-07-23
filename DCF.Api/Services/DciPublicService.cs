using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record DciSeasonDto(Guid Id, int Year);
public record DciStandingsShowRef(string ShowName, DateOnly Date, double Score);
public record DciStandingsEntry(Guid CorpsId, string CorpsName, string? CorpsIconUrl, DciStandingsShowRef Latest, IReadOnlyList<DciStandingsShowRef> Last3, double Last3Avg);
public record DciScheduleEntry(DateTimeOffset? Time, string Label, Guid? CorpsId, string? CorpsName);
public record DciScheduleShow(Guid Id, string Name, DateOnly Date, DateTimeOffset? StartTime, string? Timezone, string? Location, bool IsExhibition, IReadOnlyList<DciScheduleEntry> Schedule);
public record DciScoreResult(int Rank, Guid CorpsId, string CorpsName, double TotalScore);
public record DciScoresShow(Guid Id, string Name, DateOnly Date, bool IsExhibition, string? NoScoreReason, bool ScoresPending, IReadOnlyList<DciScoreResult> Results);
public record DciRecapScoreRow(Caption Caption, string? Judge, double RepertoireScore, double PerformanceScore, double TotalScore);
public record DciRecapCorpsEntry(Guid CorpsId, string CorpsName, string? CorpsIconUrl, IReadOnlyList<DciRecapScoreRow> Scores);
public record DciRecapShow(Guid Id, string Name, DateOnly Date, string? Location);
public record DciRecapResponse(DciRecapShow Show, IReadOnlyList<DciRecapCorpsEntry> Corps);

public class DciPublicService(DcfDbContext db) : IDciPublicService
{
    public async Task<DciSeasonDto?> GetCurrentSeasonAsync()
    {
        var active = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Active)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        if (active is not null)
        {
            return new DciSeasonDto(active.Id, active.Year);
        }

        var completed = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Completed)
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.EndDate)
            .FirstOrDefaultAsync();

        if (completed is not null)
        {
            return new DciSeasonDto(completed.Id, completed.Year);
        }

        var upcoming = await db.Seasons
            .Where(s => s.IsPublished && s.Status == SeasonStatus.Upcoming)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        return upcoming is null ? null : new DciSeasonDto(upcoming.Id, upcoming.Year);
    }

    public async Task<IReadOnlyList<DciStandingsEntry>> GetStandingsAsync(Guid seasonId)
    {
        var rows = await db.Scores
            .Where(s => s.Caption == Caption.Total && s.Show.SeasonId == seasonId)
            .Select(s => new
            {
                s.CorpsId,
                CorpsName = s.Corps.Name,
                CorpsIconPath = s.Corps.IconPath,
                ShowName = s.Show.Name,
                s.Show.Date,
                s.TotalScore
            })
            .ToListAsync();

        var entries = rows
            .GroupBy(r => r.CorpsId)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(r => r.Date).ToList();
                var last3 = ordered.Take(3)
                    .Select(r => new DciStandingsShowRef(r.ShowName, r.Date, r.TotalScore))
                    .ToList();
                var first = group.First();
                var iconUrl = first.CorpsIconPath is null ? null : "/uploads/" + first.CorpsIconPath;

                return new DciStandingsEntry(
                    group.Key, first.CorpsName, iconUrl,
                    last3[0], last3, Math.Round(last3.Average(l => l.Score), 3));
            })
            .OrderByDescending(e => e.Latest.Score)
            .ToList();

        return entries;
    }

    public async Task<IReadOnlyList<DciScheduleShow>> GetScheduleAsync(Guid seasonId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var shows = await db.Shows
            .Where(s => s.SeasonId == seasonId && s.Date >= today)
            .OrderBy(s => s.Date)
            .Include(s => s.Schedule).ThenInclude(e => e.Corps)
            .ToListAsync();

        return shows
            .Select(s => new DciScheduleShow(
                s.Id, s.Name, s.Date, s.StartTime, s.Timezone, s.Location, s.IsExhibition,
                s.Schedule
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new DciScheduleEntry(e.Time, e.Label, e.CorpsId, e.Corps?.Name))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<DciScoresShow>> GetScoresAsync(Guid seasonId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var shows = await db.Shows
            .Where(s => s.SeasonId == seasonId && s.Date < today)
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        var showIds = shows.Select(s => s.Id).ToList();

        var totalsByShow = (await db.Scores
                .Where(s => showIds.Contains(s.ShowId) && s.Caption == Caption.Total)
                .Select(s => new { s.ShowId, s.CorpsId, CorpsName = s.Corps.Name, s.TotalScore })
                .ToListAsync())
            .GroupBy(s => s.ShowId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.TotalScore).ToList());

        return shows
            .Select(s =>
            {
                var hasResults = totalsByShow.TryGetValue(s.Id, out var results) && results.Count > 0;

                return new DciScoresShow(
                    s.Id, s.Name, s.Date, s.IsExhibition, s.NoScoreReason,
                    ScoresPending: !hasResults && s.NoScoreReason is null,
                    Results: hasResults
                        ? results!.Select((r, i) => new DciScoreResult(i + 1, r.CorpsId, r.CorpsName, r.TotalScore)).ToList()
                        : []);
            })
            .ToList();
    }

    public async Task<DciRecapResponse?> GetRecapAsync(Guid showId)
    {
        var show = await db.Shows.FirstOrDefaultAsync(s => s.Id == showId);

        if (show is null)
        {
            return null;
        }

        var rows = await db.Scores
            .Where(s => s.ShowId == showId)
            .Select(s => new
            {
                s.CorpsId,
                CorpsName = s.Corps.Name,
                CorpsIconPath = s.Corps.IconPath,
                s.Caption,
                s.Judge,
                s.RepertoireScore,
                s.PerformanceScore,
                s.TotalScore
            })
            .ToListAsync();

        var corps = rows
            .GroupBy(r => r.CorpsId)
            .Select(group =>
            {
                var first = group.First();
                var iconUrl = first.CorpsIconPath is null ? null : "/uploads/" + first.CorpsIconPath;

                return new DciRecapCorpsEntry(
                    group.Key, first.CorpsName, iconUrl,
                    group.Select(r => new DciRecapScoreRow(r.Caption, r.Judge, r.RepertoireScore, r.PerformanceScore, r.TotalScore)).ToList());
            })
            .ToList();

        return new DciRecapResponse(new DciRecapShow(show.Id, show.Name, show.Date, show.Location), corps);
    }
}
