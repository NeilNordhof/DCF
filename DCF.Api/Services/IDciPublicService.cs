namespace DCF.Api.Services;

public interface IDciPublicService
{
    Task<DciSeasonDto?> GetCurrentSeasonAsync();
    Task<IReadOnlyList<DciStandingsEntry>> GetStandingsAsync(Guid seasonId);
    Task<IReadOnlyList<DciScheduleShow>> GetScheduleAsync(Guid seasonId);
    Task<IReadOnlyList<DciScoresShow>> GetScoresAsync(Guid seasonId);
    Task<DciRecapResponse?> GetRecapAsync(Guid showId);
}
