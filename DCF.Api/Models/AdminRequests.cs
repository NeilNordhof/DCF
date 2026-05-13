namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year);
public record CreateCorpsRequest(string Name);
public record CreateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset ScoresAnnouncedTime,
    Guid SeasonId,
    List<Guid> CorpsIds);
public record UpdateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
