using DCF.Data.Models;
using System.ComponentModel.DataAnnotations.Schema;

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
    public int MaxPlayers { get; set; } = 8;
    public int CorpsPerCaption { get; set; }
    public int DraftBudget { get; set; }
    public ComputedCaption[] DraftableCaptions { get; set; } = [];
    public DraftStatus DraftStatus { get; set; } = DraftStatus.NotStarted;
    public DateTimeOffset? DraftStartTime { get; set; }
    public string? DraftTimezone { get; set; }
    public string DraftOrderJson { get; set; } = "[]";
    public int CurrentPickNumber { get; set; }
    public int PickTimerSeconds { get; set; }
    public DateTimeOffset? PickDeadline { get; set; }
    public Guid? PendingPickCorpsId { get; set; }
    public ComputedCaption? PendingPickCaption { get; set; }
    public string[] IssueMessages { get; set; } = [];

    /// <summary>
    /// True when the league uses a corps-caption budget that members allocate
    /// across captions as they please, rather than a fixed number of corps per caption.
    /// </summary>
    [NotMapped]
    public bool UsesDraftBudget => DraftBudget > 0;

    /// <summary>
    /// Total number of corps-caption picks each member drafts.
    /// In budget mode this is <see cref="DraftBudget"/>; otherwise it is
    /// <see cref="CorpsPerCaption"/> multiplied by the number of draftable captions.
    /// </summary>
    [NotMapped]
    public int PicksPerMember =>
        UsesDraftBudget ? DraftBudget : CorpsPerCaption * DraftableCaptions.Length;

    public List<LeagueMemberEntity> Members { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
