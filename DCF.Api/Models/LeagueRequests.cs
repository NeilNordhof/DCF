using DCF.ScoreScraper.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    Caption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime);

public record JoinLeagueRequest(string? InviteCode);

public record SubmitPickRequest(Guid CorpsId, Caption Caption);
