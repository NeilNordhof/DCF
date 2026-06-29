namespace DCF.Api.Scraping;

public record ShowPrefillScheduleEntry(string Time24h, string Label);

public record ShowPrefillData(
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    string? StartTime,
    string? ScoresAnnouncedTime,
    string? Timezone,
    IReadOnlyList<ShowPrefillScheduleEntry> ScheduleEntries,
    string? Date);

public interface IShowInfoScraperTask
{
    Task<ShowPrefillData?> ScrapeAsync(string url);
}
