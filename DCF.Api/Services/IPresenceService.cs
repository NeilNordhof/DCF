namespace DCF.Api.Services;

public interface IPresenceService
{
    Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online);
    IReadOnlyCollection<Guid> GetOnline(Guid leagueId);
}
