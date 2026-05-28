namespace DCF.Api.Services;

public interface IStandingsService
{
    Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId);
    Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId);
    Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId);
}
