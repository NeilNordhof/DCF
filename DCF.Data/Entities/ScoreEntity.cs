using DCF.ScoreScraper.Models;

namespace DCF.Data.Entities;

public class ScoreEntity
{
    public Guid Id { get; set; }
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Caption Caption { get; set; }
    public string? Judge { get; set; }
    public double RepertoireScore { get; set; }
    public double PerformanceScore { get; set; }
    public double TotalScore { get; set; }
    public int RepertoireRank { get; set; }
    public int PerformanceRank { get; set; }
    public int TotalRank { get; set; }
}
