using DCF.Api.Models;

namespace DCF.Api.Services;

public interface IAdminService
{
    Task<bool> IsAdminAsync(string sub);
    Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync();
    Task<SeasonSummary> CreateSeasonAsync(int year, DateOnly startDate, DateOnly endDate);
    Task<SeasonDetail?> GetSeasonDetailAsync(Guid id);
    Task<bool> PublishSeasonAsync(Guid id);
    Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync();
    Task<CorpsSummary> CreateCorpsAsync(string name);
    Task<bool> SetSeasonCorpsAsync(Guid seasonId, List<Guid> corpsIds);
    Task<(bool Found, bool CanEdit)> SetSeasonCorpsOrderAsync(Guid seasonId, List<(Guid CorpsId, int? SortOrder)> orders);
    Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId);
    Task<ShowBrief> CreateShowAsync(
        Guid seasonId, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule);
    Task<bool> UpdateShowAsync(
        Guid id, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule);
    Task<bool> TriggerScrapeAsync(Guid showId);
    Task<CorpsSummary?> RenameCorpsAsync(Guid id, string name);
    Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath);
    Task<(bool Found, bool Deletable)> DeleteCorpsAsync(Guid id);
    Task<bool> UpdateSeasonDatesAsync(Guid id, DateOnly startDate, DateOnly endDate);
    Task<bool> DeleteShowAsync(Guid id);
    Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId);
}
