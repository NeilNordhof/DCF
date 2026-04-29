namespace DCF.ScoreScraper.Models
{
    public class Score
    {
        public required Guid Id { get; set; }
        public required Corps Corps { get; set; }
        public required Show Show { get; set; }
        public required Caption Caption { get; set; }
        public string? Judge { get; set; }
        public double RepertoireScore { get; set; }
        public double PerformanceScore { get; set; }
        public required double TotalScore { get; set; }
        public int RepertoireRank { get; set; }
        public int PerformanceRank { get; set; }
        public required int TotalRank { get; set; }
    }
}
