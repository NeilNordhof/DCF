namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
public record CreateCorpsRequest(string Name);
public record CreateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset ScoresAnnouncedTime,
    string? Timezone,
    List<Guid> CorpsIds);
public record UpdateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset ScoresAnnouncedTime,
    string? Timezone,
    List<Guid> CorpsIds);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
public record CorpsOrderItem(Guid CorpsId, int? SortOrder);
public record SetCorpsOrderRequest(List<CorpsOrderItem> Orders);
public record RenameCorpsRequest(string Name);
public record UpdateSeasonDatesRequest(DateOnly StartDate, DateOnly EndDate);
