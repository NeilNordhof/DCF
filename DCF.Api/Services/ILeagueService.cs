using DCF.Data.Models;

namespace DCF.Api.Services;

public interface ILeagueService
{
    Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub);
    Task<LeagueBrief?> CreateAsync(string userSub, string name, bool isPublic, int corpsPerCaption, ComputedCaption[] draftableCaptions, DateTimeOffset? draftStartTime);
    Task<JoinResult> JoinAsync(Guid leagueId, string userSub, string? inviteCode);
    Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode);
}
