using System.Text;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wordrise.Web.Domain;

namespace Wordrise.Web.Data;

public class WordriseDbContext(DbContextOptions<WordriseDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Word> Words => Set<Word>();
    public DbSet<DailyWord> DailyWords => Set<DailyWord>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<PendingReview> PendingReviews => Set<PendingReview>();
    public DbSet<StreakFreezeDay> StreakFreezeDays => Set<StreakFreezeDay>();
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();
    public DbSet<SprintSession> SprintSessions => Set<SprintSession>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    private const string LevelCheck = "IN ('A2','B1','B2')";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_level", $"level {LevelCheck}");
                t.HasCheckConstraint("ck_users_email_lower", "email = lower(email)");
                t.HasCheckConstraint("ck_users_daily_goal", "daily_goal BETWEEN 1 AND 20");
                t.HasCheckConstraint("ck_users_streak_freezes", "streak_freezes BETWEEN 0 AND 2");
            });
            // Database defaults let the migration fill existing rows. The sentinel is the CLR default of each
            // property so EF always sends explicit values (otherwise "false" would be replaced by the default).
            e.Property(x => x.DailyGoal).HasDefaultValue(3).HasSentinel(0);
            e.Property(x => x.SoundEnabled).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.ShowInLeagues).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(2).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Word>(e =>
        {
            e.ToTable("words", t =>
            {
                t.HasCheckConstraint("ck_words_level", $"level {LevelCheck}");
                t.HasCheckConstraint("ck_words_text_format", "text ~ '^[a-z]{4,10}$'");
            });
            e.Property(x => x.Text).HasMaxLength(10).IsRequired();
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(2).IsRequired();
            e.Property(x => x.PartOfSpeech).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.DefinitionEn).HasMaxLength(300).IsRequired();
            e.Property(x => x.ExampleSentence).HasMaxLength(300).IsRequired();
            e.Property(x => x.TranslationEs).HasMaxLength(100).IsRequired();
            e.Property(x => x.Emoji).HasMaxLength(16);
            e.HasIndex(x => x.Text).IsUnique();
            e.HasIndex(x => new { x.Level, x.IsDailyAnswer });
        });

        modelBuilder.Entity<DailyWord>(e =>
        {
            e.ToTable("daily_words", t => t.HasCheckConstraint("ck_daily_words_level", $"level {LevelCheck}"));
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(2).IsRequired();
            e.HasIndex(x => new { x.Date, x.Level }).IsUnique();
            e.HasOne(x => x.Word).WithMany().HasForeignKey(x => x.WordId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Round>(e =>
        {
            e.ToTable("rounds", t =>
            {
                t.HasCheckConstraint("ck_rounds_attempts", "attempts_used BETWEEN 0 AND 6");
                t.HasCheckConstraint("ck_rounds_hints", "hints_unlocked BETWEEN 1 AND 4");
                t.HasCheckConstraint("ck_rounds_manual_hints", "manual_hints_used BETWEEN 0 AND 3");
                t.HasCheckConstraint("ck_rounds_score", "score >= 0");
                t.HasCheckConstraint("ck_rounds_xp", "xp >= 0");
                t.HasCheckConstraint("ck_rounds_mode", "mode IN ('Daily','Review','Practice','Listening')");
                t.HasCheckConstraint("ck_rounds_daily_link",
                    "(mode = 'Daily' AND daily_word_id IS NOT NULL) OR (mode <> 'Daily' AND daily_word_id IS NULL)");
            });
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12).IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.Rounds).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Word).WithMany().HasForeignKey(x => x.WordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DailyWord).WithMany().HasForeignKey(x => x.DailyWordId).OnDelete(DeleteBehavior.Restrict);

            // One daily round per user per daily word (prevents duplicated rounds on double click / race).
            e.HasIndex(x => new { x.UserId, x.DailyWordId }).IsUnique().HasFilter("daily_word_id IS NOT NULL");
            e.HasIndex(x => new { x.UserId, x.PlayedOn });
        });

        modelBuilder.Entity<Attempt>(e =>
        {
            e.ToTable("attempts", t => t.HasCheckConstraint("ck_attempts_number", "number BETWEEN 1 AND 6"));
            e.Property(x => x.GuessText).HasMaxLength(10).IsRequired();
            e.Property(x => x.Feedback).HasMaxLength(10).IsRequired();
            e.HasOne(x => x.Round).WithMany(r => r.Attempts).HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RoundId, x.Number }).IsUnique();
        });

        modelBuilder.Entity<PendingReview>(e =>
        {
            e.ToTable("pending_reviews", t => t.HasCheckConstraint("ck_pending_reviews_stage", "stage >= 0"));
            e.HasOne(x => x.User).WithMany(u => u.PendingReviews).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Word).WithMany().HasForeignKey(x => x.WordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.WordId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.NextReviewOn });
        });

        modelBuilder.Entity<StreakFreezeDay>(e =>
        {
            e.ToTable("streak_freeze_days");
            e.HasKey(x => new { x.UserId, x.Date });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAchievement>(e =>
        {
            e.ToTable("user_achievements");
            e.HasKey(x => new { x.UserId, x.Code });
            e.Property(x => x.Code).HasMaxLength(40);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SprintSession>(e =>
        {
            e.ToTable("sprint_sessions", t =>
            {
                t.HasCheckConstraint("ck_sprint_sessions_level", $"level {LevelCheck}");
                t.HasCheckConstraint("ck_sprint_sessions_counts", "score >= 0 AND skipped >= 0 AND xp >= 0");
                t.HasCheckConstraint("ck_sprint_sessions_window", "ends_at > started_at");
            });
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(2).IsRequired();
            e.Property(x => x.CurrentLetters).HasMaxLength(10).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CurrentWord).WithMany().HasForeignKey(x => x.CurrentWordId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.PlayedOn });
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.ToTable("groups", t =>
            {
                t.HasCheckConstraint("ck_groups_code", "code ~ '^[A-Z2-9]{6}$'");
                t.HasCheckConstraint("ck_groups_name", "char_length(trim(name)) BETWEEN 3 AND 40");
            });
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.Property(x => x.Code).HasMaxLength(6).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GroupMember>(e =>
        {
            e.ToTable("group_members");
            e.HasKey(x => new { x.GroupId, x.UserId });
            e.HasOne(x => x.Group).WithMany(g => g.Members).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        ApplySnakeCaseNames(modelBuilder);
    }

    /// <summary>Column/key/index names in snake_case (table names are set explicitly above).</summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.IsPrimaryKey()
                    ? $"pk_{table}"
                    : $"ak_{table}_{string.Join("_", key.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(
                    $"fk_{table}_{fk.PrincipalEntityType.GetTableName()}_{string.Join("_", fk.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(
                    $"ix_{table}_{string.Join("_", index.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
