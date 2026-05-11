namespace DCF.Data.Entities;

public class SeasonCorpsEntity
{
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
}
