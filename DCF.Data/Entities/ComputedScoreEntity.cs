namespace DCF.Data.Entities;

public class ComputedScoreEntity
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public double GeneralEffectCombined { get; set; }
    public double GeneralEffect1 { get; set; }
    public double GeneralEffect2 { get; set; }
    public double VisualCombined { get; set; }
    public double Visual { get; set; }
    public double Colorguard { get; set; }
    public double VisualProficiency { get; set; }
    public double VisualAnalysis { get; set; }
    public double MusicCombined { get; set; }
    public double Brass { get; set; }
    public double Percussion { get; set; }
    public double MusicAnalysis { get; set; }
}
