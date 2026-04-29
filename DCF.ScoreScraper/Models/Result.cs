namespace DCF.ScoreScraper.Models
{
    public class Result
    {
        public required int Id { get; set; }
        public required Corps Corps { get; set; }
        public required Show Show { get; set; }
        public Score? GeneralEffect1_1 { get; set; }
        public Score? GeneralEffect1_2 { get; set; }
        public Score? GeneralEffect2_1 { get; set; }
        public Score? GeneralEffect2_2 { get; set; }
        public Score? GeneralEffect { get; set; }
        public Score? VisualAnalysis { get; set; }
        public Score? VisualProficiency { get; set; }
        public Score? ColorGuard { get; set; }
        public Score? Visual { get; set; }
        public Score? Brass { get; set; }
        public Score? MusicAnalysis1 { get; set; }
        public Score? MusicAnalysis2 { get; set; }
        public Score? Percussion1 { get; set; }
        public Score? Music { get; set; }
        public Score? SubTotal { get; set; }
        public Score? Penalty { get; set; }
        public Score? Total { get; set; }
    }
}
