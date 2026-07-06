using System.Text.Json;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Data;

public class DcfDbContext(DbContextOptions<DcfDbContext> options) : DbContext(options)
{
    public DbSet<SeasonEntity> Seasons => Set<SeasonEntity>();
    public DbSet<CorpsEntity> Corps => Set<CorpsEntity>();
    public DbSet<SeasonCorpsEntity> SeasonCorps => Set<SeasonCorpsEntity>();
    public DbSet<ShowEntity> Shows => Set<ShowEntity>();
    public DbSet<ShowCorpsEntity> ShowCorps => Set<ShowCorpsEntity>();
    public DbSet<ScoreEntity> Scores => Set<ScoreEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<LeagueEntity> Leagues => Set<LeagueEntity>();
    public DbSet<LeagueMemberEntity> LeagueMembers => Set<LeagueMemberEntity>();
    public DbSet<DraftPickEntity> DraftPicks => Set<DraftPickEntity>();
    public DbSet<ComputedScoreEntity> ComputedScores => Set<ComputedScoreEntity>();
    public DbSet<ShowScheduleEntryEntity> ShowScheduleEntries => Set<ShowScheduleEntryEntity>();
    public DbSet<RememberMeTokenEntity> RememberMeTokens => Set<RememberMeTokenEntity>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<SeasonCorpsEntity>().HasKey(e => new { e.SeasonId, e.CorpsId });
        mb.Entity<ShowCorpsEntity>().HasKey(e => new { e.ShowId, e.CorpsId });
        mb.Entity<LeagueMemberEntity>().HasKey(e => new { e.LeagueId, e.UserId });

        mb.Entity<DraftPickEntity>()
            .HasIndex(e => new { e.LeagueId, e.CorpsId, e.Caption })
            .IsUnique();

        mb.Entity<ScoreEntity>()
            .HasIndex(e => new { e.CorpsId, e.ShowId, e.Caption, e.Judge })
            .IsUnique();

        mb.Entity<UserEntity>()
            .HasIndex(e => e.Auth0Sub)
            .IsUnique();

        mb.Entity<LeagueEntity>()
            .HasIndex(e => e.InviteCode)
            .IsUnique();

        mb.Entity<LeagueEntity>()
            .HasOne(e => e.Commissioner)
            .WithMany(u => u.CommissionedLeagues)
            .HasForeignKey(e => e.CommissionerUserId);

        mb.Entity<ScoreEntity>().HasIndex(e => e.ShowId);
        mb.Entity<DraftPickEntity>().HasIndex(e => new { e.UserId, e.LeagueId });
        mb.Entity<SeasonEntity>().HasIndex(e => e.Year).IsUnique();
        mb.Entity<CorpsEntity>().HasIndex(e => e.Name).IsUnique();

        mb.Entity<LeagueEntity>()
            .Property(e => e.DraftableCaptions)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<ComputedCaption[]>(v, JsonSerializerOptions.Default) ?? Array.Empty<ComputedCaption>());

        mb.Entity<LeagueEntity>()
            .Property(e => e.IssueMessages)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<string[]>(v, JsonSerializerOptions.Default) ?? Array.Empty<string>());

        mb.Entity<ComputedScoreEntity>()
            .HasIndex(e => new { e.ShowId, e.CorpsId })
            .IsUnique();

        mb.Entity<ComputedScoreEntity>()
            .HasIndex(e => e.SeasonId);

        mb.Entity<ShowScheduleEntryEntity>().HasIndex(e => e.ShowId);

        mb.Entity<RememberMeTokenEntity>().HasIndex(e => e.TokenHash).IsUnique();
        mb.Entity<RememberMeTokenEntity>().HasIndex(e => e.UserId);
    }
}
