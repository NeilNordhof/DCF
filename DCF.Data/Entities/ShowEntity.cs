namespace DCF.Data.Entities;

public class ShowEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTimeOffset ScoresAnnouncedTime { get; set; }
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
}
