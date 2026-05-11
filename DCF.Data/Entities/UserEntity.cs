namespace DCF.Data.Entities;

public class UserEntity
{
    public Guid Id { get; set; }
    public string Auth0Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    public List<LeagueMemberEntity> LeagueMemberships { get; set; } = [];
    public List<LeagueEntity> CommissionedLeagues { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
