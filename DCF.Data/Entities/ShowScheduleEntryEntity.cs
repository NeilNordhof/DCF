namespace DCF.Data.Entities;

public class ShowScheduleEntryEntity
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public int SortOrder { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? CorpsId { get; set; }
    public CorpsEntity? Corps { get; set; }
}
