using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record SeasonSummary(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished);
public record SeasonDetail(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished, IEnumerable<Guid> CorpsIds);
public record CorpsSummary(Guid Id, string Name, string? IconUrl);
public record ShowSummary(Guid Id, string Name, string Url, DateOnly Date, DateTimeOffset? StartTime, DateTimeOffset ScoresAnnouncedTime, IEnumerable<Guid> CorpsIds);
public record ShowBrief(Guid Id, string Name);

public class AdminService(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttService mqtt,
    ISeasonStatusService seasonStatus) : IAdminService
{
    public async Task<bool> IsAdminAsync(string sub)
    {
        return await db.Users.AnyAsync(u => u.Auth0Sub == sub && u.IsAdmin);
    }

    public async Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync()
    {
        return await db.Seasons
            .OrderByDescending(s => s.Year)
            .Select(s => new SeasonSummary(s.Id, s.Year, s.StartDate, s.EndDate, s.Status, s.IsPublished))
            .ToListAsync();
    }

    public async Task<SeasonSummary> CreateSeasonAsync(int year, DateOnly startDate, DateOnly endDate)
    {
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = year,
            StartDate = startDate,
            EndDate = endDate
        };

        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        seasonStatus.ScheduleSeason(season);

        return new SeasonSummary(season.Id, season.Year, season.StartDate, season.EndDate, season.Status, season.IsPublished);
    }

    public async Task<SeasonDetail?> GetSeasonDetailAsync(Guid id)
    {
        var season = await db.Seasons
            .Include(s => s.SeasonCorps)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (season is null)
        {
            return null;
        }

        return new SeasonDetail(
            season.Id, season.Year, season.StartDate, season.EndDate,
            season.Status, season.IsPublished,
            season.SeasonCorps.Select(sc => sc.CorpsId));
    }

    public async Task<bool> PublishSeasonAsync(Guid id)
    {
        var season = await db.Seasons.FindAsync(id);

        if (season is null)
        {
            return false;
        }

        season.IsPublished = true;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync()
    {
        var corps = await db.Corps
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.IconPath })
            .ToListAsync();

        return corps
            .Select(c => new CorpsSummary(c.Id, c.Name, c.IconPath != null ? $"/uploads/{c.IconPath}" : null))
            .ToList();
    }

    public async Task<CorpsSummary> CreateCorpsAsync(string name)
    {
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = name };
        db.Corps.Add(corps);

        await db.SaveChangesAsync();

        return new CorpsSummary(corps.Id, corps.Name, null);
    }

    public async Task<bool> SetSeasonCorpsAsync(Guid seasonId, List<Guid> corpsIds)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == seasonId))
        {
            return false;
        }

        var existing = await db.SeasonCorps.Where(sc => sc.SeasonId == seasonId).ToListAsync();
        db.SeasonCorps.RemoveRange(existing);
        db.SeasonCorps.AddRange(corpsIds.Select(cId =>
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = cId }));

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId)
    {
        return await db.Shows
            .Where(s => s.SeasonId == seasonId)
            .Include(s => s.ShowCorps)
            .OrderBy(s => s.Date)
            .Select(s => new ShowSummary(s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime,
                s.ShowCorps.Select(sc => sc.CorpsId)))
            .ToListAsync();
    }

    public async Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url,
        DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var season = await db.Seasons.FindAsync(seasonId)
            ?? throw new InvalidOperationException("Season not found.");

        if (date < season.StartDate || date > season.EndDate)
        {
            throw new InvalidOperationException($"Show date must be within the season range ({season.StartDate}–{season.EndDate}).");
        }

        if (date < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new InvalidOperationException("Show date cannot be in the past.");
        }

        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Date = date,
            StartTime = startTime,
            ScoresAnnouncedTime = scoresAnnouncedTime,
            SeasonId = seasonId
        };
        db.Shows.Add(show);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));

        await db.SaveChangesAsync();

        scrapeScheduler.ScheduleScrape(show);

        return new ShowBrief(show.Id, show.Name);
    }

    public async Task<bool> UpdateShowAsync(Guid id, string name, string url,
        DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        if (show.StartTime.HasValue && show.StartTime.Value <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        show.Name = name;
        show.Url = url;
        show.Date = date;
        show.StartTime = startTime;
        show.ScoresAnnouncedTime = scoresAnnouncedTime;

        var existing = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(existing);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = id, CorpsId = cId }));

        await db.SaveChangesAsync();

        var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
        scrapeScheduler.ScheduleScrape(updatedShow);

        return true;
    }

    public async Task<bool> TriggerScrapeAsync(Guid showId)
    {
        var show = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == showId);

        if (show is null)
        {
            return false;
        }

        await scrapeScheduler.ExecuteScrapeAsync(show);

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = showId });

        return true;
    }

    public async Task<CorpsSummary?> RenameCorpsAsync(Guid id, string name)
    {
        var corps = await db.Corps.FindAsync(id);

        if (corps is null)
        {
            return null;
        }

        corps.Name = name;

        await db.SaveChangesAsync();

        return new CorpsSummary(corps.Id, corps.Name, corps.IconPath != null ? $"/uploads/{corps.IconPath}" : null);
    }

    public async Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath)
    {
        var corps = await db.Corps.FindAsync(id);

        if (corps is null)
        {
            return (false, null);
        }

        var oldPath = corps.IconPath;
        corps.IconPath = iconPath;

        await db.SaveChangesAsync();

        return (true, oldPath);
    }

    public async Task<(bool Found, bool Deletable)> DeleteCorpsAsync(Guid id)
    {
        var corps = await db.Corps.FindAsync(id);

        if (corps is null)
        {
            return (false, false);
        }

        var seasonIds = await db.SeasonCorps
            .Where(sc => sc.CorpsId == id)
            .Select(sc => sc.SeasonId)
            .ToListAsync();

        var inPublishedSeason = await db.Seasons
            .AnyAsync(s => seasonIds.Contains(s.Id) && s.IsPublished);

        if (inPublishedSeason)
        {
            return (true, false);
        }

        var unpublishedSeasonCorps = await db.SeasonCorps.Where(sc => sc.CorpsId == id).ToListAsync();
        db.SeasonCorps.RemoveRange(unpublishedSeasonCorps);
        db.Corps.Remove(corps);

        await db.SaveChangesAsync();

        return (true, true);
    }

    public async Task<bool> UpdateSeasonDatesAsync(Guid id, DateOnly startDate, DateOnly endDate)
    {
        var season = await db.Seasons.FindAsync(id);

        if (season is null || season.IsPublished)
        {
            return false;
        }

        season.StartDate = startDate;
        season.EndDate = endDate;

        await db.SaveChangesAsync();

        seasonStatus.ScheduleSeason(season);

        return true;
    }

    public async Task<bool> DeleteShowAsync(Guid id)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        var showCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(showCorps);
        db.Shows.Remove(show);

        await db.SaveChangesAsync();

        return true;
    }
}
