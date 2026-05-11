namespace DCF.Data.Entities;

public class LeagueMemberEntity
{
    public Guid LeagueId { get; set; }
    public LeagueEntity League { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}
