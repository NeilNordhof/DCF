namespace DCF.Api.Services;

public interface IAdminService
{
    Task<bool> IsAdminAsync(string sub);
    Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync();
    Task<SeasonSummary> CreateSeasonAsync(int year);
    Task<bool> ActivateSeasonAsync(Guid id);
    Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync();
    Task<CorpsSummary> CreateCorpsAsync(string name);
    Task<bool> SetSeasonCorpsAsync(Guid seasonId, List<Guid> corpsIds);
    Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId);
    Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url, DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
    Task<bool> UpdateShowAsync(Guid id, string name, string url, DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
    Task<bool> TriggerScrapeAsync(Guid showId);
}
