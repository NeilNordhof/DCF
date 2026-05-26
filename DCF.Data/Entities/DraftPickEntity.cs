using DCF.Data.Models;

namespace DCF.Data.Entities;

public class DraftPickEntity
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public LeagueEntity League { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public ComputedCaption Caption { get; set; }
    public int PickNumber { get; set; }
    public int RoundNumber { get; set; }
}
