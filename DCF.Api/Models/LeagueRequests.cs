using DCF.Data.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime);

public record JoinLeagueRequest(string? InviteCode);

public record SubmitPickRequest(Guid CorpsId, ComputedCaption Caption);
