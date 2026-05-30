namespace DCF.Data.Entities;

public class CorpsEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconPath { get; set; }

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
