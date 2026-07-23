namespace DCF.Api.Services;

public interface IDciPublicService
{
    Task<DciSeasonDto?> GetCurrentSeasonAsync();
    Task<IReadOnlyList<DciStandingsEntry>> GetStandingsAsync(Guid seasonId);
    Task<IReadOnlyList<DciScheduleShow>> GetScheduleAsync(Guid seasonId);
}
