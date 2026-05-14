using DCF.Data.Models;

namespace DCF.Data.Entities;

public class LeagueEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CommissionerUserId { get; set; }
    public UserEntity Commissioner { get; set; } = null!;
    public bool IsPublic { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public int CorpsPerCaption { get; set; }
    public Caption[] DraftableCaptions { get; set; } = [];
    public DraftStatus DraftStatus { get; set; } = DraftStatus.NotStarted;
    public DateTimeOffset? DraftStartTime { get; set; }
    public string DraftOrderJson { get; set; } = "[]";
    public int CurrentPickNumber { get; set; }

    public List<LeagueMemberEntity> Members { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
