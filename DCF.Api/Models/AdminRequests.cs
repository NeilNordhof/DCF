namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
public record CreateCorpsRequest(string Name);
public record ShowScheduleEntryRequest(DateTimeOffset? Time, string Label, Guid? CorpsId);
public record CreateShowRequest(
    string Name,
    string? Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    List<Guid> CorpsIds,
    List<ShowScheduleEntryRequest> Schedule);
public record UpdateShowRequest(
    string Name,
    string? Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    List<Guid> CorpsIds,
    List<ShowScheduleEntryRequest> Schedule);
public record SetNoScoreReasonRequest(string? Reason);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
public record CorpsOrderItem(Guid CorpsId, int? SortOrder);
public record SetCorpsOrderRequest(List<CorpsOrderItem> Orders);
public record RenameCorpsRequest(string Name);
public record UpdateSeasonDatesRequest(DateOnly StartDate, DateOnly EndDate);
public record ShowScheduleEntryResponse(DateTimeOffset? Time, string Label, Guid? CorpsId);
public record ShowPrefillScheduleEntryResponse(string? Time, string Label, Guid? CorpsId);
public record ShowPrefillResponse(
    string? Location,
    double? Latitude,
    double? Longitude,
    string? StartTime,
    string? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    List<Guid> CorpsIds,
    List<ShowPrefillScheduleEntryResponse> Schedule,
    string? Date);
