using System.Text.RegularExpressions;
using DCF.Api.Models;
using DCF.Api.Scraping;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record SeasonSummary(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished);
public record SeasonDetail(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished, IEnumerable<Guid> CorpsIds, IReadOnlyDictionary<Guid, int> CorpsSortOrders);
public record CorpsSummary(Guid Id, string Name, string? IconUrl);
public record ShowSummary(
    Guid Id, string Name, string? Url, DateOnly Date, DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime, string? Timezone, bool IsExhibition,
    string? Location, double? Latitude, double? Longitude,
    ScrapeStatus ScrapeStatus, DateTimeOffset? LastScrapeAttemptAt, string? ScrapeError,
    IEnumerable<Guid> CorpsIds, IEnumerable<ShowScheduleEntryResponse> Schedule);
public record ShowBrief(Guid Id, string Name);

public class AdminService(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttService mqtt,
    ISeasonStatusService seasonStatus,
    IShowInfoScraperTask showInfoScraper) : IAdminService
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

        var orderedCorpsIds = season.SeasonCorps
            .OrderBy(sc => sc.SortOrder == null)
            .ThenBy(sc => sc.SortOrder)
            .Select(sc => sc.CorpsId);

        var sortOrders = season.SeasonCorps
            .Where(sc => sc.SortOrder.HasValue)
            .ToDictionary(sc => sc.CorpsId, sc => sc.SortOrder!.Value);

        return new SeasonDetail(
            season.Id, season.Year, season.StartDate, season.EndDate,
            season.Status, season.IsPublished,
            orderedCorpsIds,
            sortOrders);
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
        var shows = await db.Shows
            .Where(s => s.SeasonId == seasonId)
            .Include(s => s.ShowCorps)
            .Include(s => s.Schedule)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ToListAsync();

        return shows.Select(s => new ShowSummary(
            s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime, s.Timezone,
            s.IsExhibition, s.Location, s.Latitude, s.Longitude,
            s.ScrapeStatus, s.LastScrapeAttemptAt, s.ScrapeError,
            s.ShowCorps.Select(sc => sc.CorpsId),
            s.Schedule.OrderBy(e => e.SortOrder)
                .Select(e => new ShowScheduleEntryResponse(e.Time, e.Label, e.CorpsId))))
            .ToList();
    }

    public async Task<ShowBrief> CreateShowAsync(
        Guid seasonId, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule)
    {
        var season = await db.Seasons.FindAsync(seasonId)
            ?? throw new InvalidOperationException("Season not found.");

        if (date < season.StartDate || date > season.EndDate)
        {
            throw new InvalidOperationException($"Show date must be within the season range ({season.StartDate}–{season.EndDate}).");
        }

        if (date < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-10)))
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
            Timezone = timezone,
            IsExhibition = isExhibition,
            Location = location,
            Latitude = latitude,
            Longitude = longitude,
            SeasonId = seasonId
        };

        db.Shows.Add(show);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));
        db.ShowScheduleEntries.AddRange(schedule.Select((entry, i) =>
            new ShowScheduleEntryEntity
            {
                Id = Guid.NewGuid(),
                ShowId = show.Id,
                SortOrder = i,
                Time = entry.Time,
                Label = entry.Label,
                CorpsId = entry.CorpsId
            }));

        await db.SaveChangesAsync();

        scrapeScheduler?.ScheduleScrape(show);

        return new ShowBrief(show.Id, show.Name);
    }

    public async Task<bool> UpdateShowAsync(
        Guid id, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        if (show.ScrapeStatus == ScrapeStatus.Succeeded)
        {
            return false;
        }

        show.Name = name;
        show.Url = url;
        show.Date = date;
        show.StartTime = startTime;
        show.ScoresAnnouncedTime = scoresAnnouncedTime;
        show.Timezone = timezone;
        show.IsExhibition = isExhibition;
        show.Location = location;
        show.Latitude = latitude;
        show.Longitude = longitude;

        var existingCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(existingCorps);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = id, CorpsId = cId }));

        var existingSchedule = await db.ShowScheduleEntries.Where(e => e.ShowId == id).ToListAsync();
        db.ShowScheduleEntries.RemoveRange(existingSchedule);
        db.ShowScheduleEntries.AddRange(schedule.Select((entry, i) =>
            new ShowScheduleEntryEntity
            {
                Id = Guid.NewGuid(),
                ShowId = id,
                SortOrder = i,
                Time = entry.Time,
                Label = entry.Label,
                CorpsId = entry.CorpsId
            }));

        await db.SaveChangesAsync();

        var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
        scrapeScheduler?.ScheduleScrape(updatedShow);

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

    public async Task<(bool Found, bool CanEdit)> SetSeasonCorpsOrderAsync(Guid seasonId, List<(Guid CorpsId, int? SortOrder)> orders)
    {
        var season = await db.Seasons.FindAsync(seasonId);

        if (season is null)
        {
            return (false, false);
        }

        if (season.IsPublished)
        {
            return (true, false);
        }

        var seasonCorps = await db.SeasonCorps
            .Where(sc => sc.SeasonId == seasonId)
            .ToListAsync();

        var orderMap = orders.ToDictionary(o => o.CorpsId, o => o.SortOrder);

        foreach (var sc in seasonCorps)
        {
            if (orderMap.TryGetValue(sc.CorpsId, out var order))
            {
                sc.SortOrder = order;
            }
        }

        await db.SaveChangesAsync();

        return (true, true);
    }

    public async Task<bool> DeleteShowAsync(Guid id)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        var showCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        var scheduleEntries = await db.ShowScheduleEntries.Where(e => e.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(showCorps);
        db.ShowScheduleEntries.RemoveRange(scheduleEntries);
        db.Shows.Remove(show);

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId)
    {
        var season = await db.Seasons.FindAsync(seasonId);

        if (season is null)
        {
            return null;
        }

        var slug = Slugify(showName);
        var eventsUrl = $"https://www.dci.org/events/{season.Year}-{slug}/";

        var prefillData = await showInfoScraper.ScrapeAsync(eventsUrl);

        if (prefillData is null)
        {
            return null;
        }

        var seasonCorpsList = await db.Corps
            .Where(c => db.SeasonCorps.Any(sc => sc.SeasonId == seasonId && sc.CorpsId == c.Id))
            .ToListAsync();

        var corpsIds = new List<Guid>();
        var scheduleEntries = new List<ShowPrefillScheduleEntryResponse>();

        foreach (var entry in prefillData.ScheduleEntries)
        {
            var corpsName = StripCity(entry.Label);
            var corpsMatch = seasonCorpsList.FirstOrDefault(c =>
                c.Name.Equals(corpsName, StringComparison.OrdinalIgnoreCase));

            var corpsId = corpsMatch?.Id;

            if (corpsId.HasValue && !corpsIds.Contains(corpsId.Value))
            {
                corpsIds.Add(corpsId.Value);
            }

            scheduleEntries.Add(new ShowPrefillScheduleEntryResponse(entry.Time24h, entry.Label, corpsId));
        }

        return new ShowPrefillResponse(
            prefillData.Location,
            prefillData.Latitude,
            prefillData.Longitude,
            prefillData.StartTime,
            prefillData.ScoresAnnouncedTime,
            prefillData.Timezone,
            prefillData.IsExhibition,
            corpsIds,
            scheduleEntries);
    }

    private static string Slugify(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-");

        return slug.Trim('-');
    }

    private static string StripCity(string label)
    {
        var dashIndex = label.IndexOf(" - ", StringComparison.Ordinal);

        return dashIndex >= 0 ? label[..dashIndex].Trim() : label.Trim();
    }
}
