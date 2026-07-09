using DCF.Data.Models;

namespace DCF.Data.Entities;

public class ShowEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? ScoresAnnouncedTime { get; set; }
    public string? Timezone { get; set; }
    public bool IsExhibition { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public ScrapeStatus ScrapeStatus { get; set; } = ScrapeStatus.NotStarted;
    public DateTimeOffset? LastScrapeAttemptAt { get; set; }
    public string? ScrapeError { get; set; }
    public string? NoScoreReason { get; set; }
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
    public List<ShowScheduleEntryEntity> Schedule { get; set; } = [];
}
