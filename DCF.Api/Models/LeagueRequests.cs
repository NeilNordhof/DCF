using DCF.Data.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    int MaxPlayers,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime,
    string? DraftTimezone);

public record JoinLeagueRequest(string? InviteCode);

public record UpdateLeagueRequest(
    int CorpsPerCaption,
    int MaxPlayers,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime,
    string? DraftTimezone);

public record SubmitPickRequest(Guid CorpsId, ComputedCaption Caption);
