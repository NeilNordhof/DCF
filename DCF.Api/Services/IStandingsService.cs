namespace DCF.Api.Services;

public interface IStandingsService
{
    Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId);
}
