using DCF.Api.Models;
using DCF.Data.Entities;
using DCF.Data.Models;

namespace DCF.Api.Services;

public interface ILeagueService
{
    Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub);
    Task<LeagueEntity> CreateAsync(string name, bool isPublic, int corpsPerCaption, int maxPlayers, List<ComputedCaption> captions, string userSub, DateTimeOffset? draftStartTime = null);
    Task<JoinResult> JoinAsync(Guid leagueId, string userSub, string? inviteCode);
    Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode);
    Task<IReadOnlyList<PublicLeagueSummary>> GetPublicLeaguesAsync();
    Task<Guid?> LookupByCodeAsync(string code);
    Task UpdateAsync(Guid leagueId, UpdateLeagueRequest req, string userSub);
    Task<string> RefreshInviteCodeAsync(Guid leagueId, string userSub);
}
