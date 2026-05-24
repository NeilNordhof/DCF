using DCF.Data.Models;

namespace DCF.Api.Services;

public interface IDraftService
{
    Task OpenDraftAsync(Guid leagueId);
    Task OpenDraftAsync(Guid leagueId, string userSub);
    Task StartDraftAsync(Guid leagueId);
    Task StartDraftAsync(Guid leagueId, string userSub);
    Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, Caption caption);
    Task SkipCurrentPickAsync(Guid leagueId, string userSub);
}
