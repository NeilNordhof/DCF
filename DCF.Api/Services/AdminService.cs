using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record SeasonSummary(Guid Id, int Year, bool IsActive);
public record CorpsSummary(Guid Id, string Name);
public record ShowSummary(Guid Id, string Name, string Url, DateOnly Date, DateTimeOffset ScoresAnnouncedTime, IEnumerable<Guid> CorpsIds);
public record ShowBrief(Guid Id, string Name);

public class AdminService(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttPublisherService mqtt) : IAdminService
{
    public async Task<bool> IsAdminAsync(string sub)
    {
        return await db.Users.AnyAsync(u => u.Auth0Sub == sub && u.IsAdmin);
    }

    public async Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync()
    {
        return await db.Seasons
            .OrderByDescending(s => s.Year)
            .Select(s => new SeasonSummary(s.Id, s.Year, s.IsActive))
            .ToListAsync();
    }

    public async Task<SeasonSummary> CreateSeasonAsync(int year)
    {
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = year };
        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        return new SeasonSummary(season.Id, season.Year, season.IsActive);
    }

    public async Task<bool> ActivateSeasonAsync(Guid id)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == id))
        {
            return false;
        }

        await db.Seasons.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        await db.Seasons.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));

        return true;
    }

    public async Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync()
    {
        return await db.Corps
            .OrderBy(c => c.Name)
            .Select(c => new CorpsSummary(c.Id, c.Name))
            .ToListAsync();
    }

    public async Task<CorpsSummary> CreateCorpsAsync(string name)
    {
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = name };
        db.Corps.Add(corps);

        await db.SaveChangesAsync();

        return new CorpsSummary(corps.Id, corps.Name);
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
            .Select(s => new ShowSummary(s.Id, s.Name, s.Url, s.Date, s.ScoresAnnouncedTime,
                s.ShowCorps.Select(sc => sc.CorpsId)))
            .ToListAsync();
    }

    public async Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url,
        DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = name, Url = url,
            Date = date, ScoresAnnouncedTime = scoresAnnouncedTime, SeasonId = seasonId
        };
        db.Shows.Add(show);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));

        await db.SaveChangesAsync();

        scrapeScheduler.ScheduleScrape(show);

        return new ShowBrief(show.Id, show.Name);
    }

    public async Task<bool> UpdateShowAsync(Guid id, string name, string url,
        DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        show.Name = name;
        show.Url = url;
        show.Date = date;
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
}
