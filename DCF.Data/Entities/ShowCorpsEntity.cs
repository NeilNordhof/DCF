namespace DCF.Data.Entities;

public class ShowCorpsEntity
{
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
}
