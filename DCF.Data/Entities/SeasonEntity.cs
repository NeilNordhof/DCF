using DCF.Data.Models;

namespace DCF.Data.Entities;

public class SeasonEntity
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public SeasonStatus Status { get; set; } = SeasonStatus.Upcoming;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsPublished { get; set; }

    public List<ShowEntity> Shows { get; set; } = [];
    public List<LeagueEntity> Leagues { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
}
