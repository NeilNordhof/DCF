namespace DCF.Data.Entities;

public class SeasonEntity
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public bool IsActive { get; set; }

    public List<ShowEntity> Shows { get; set; } = [];
    public List<LeagueEntity> Leagues { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
}
