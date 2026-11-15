using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

public sealed record GoalProgress(int Target, int Done)
{
    public bool Met => Done >= Target;
    public int Percent => Target <= 0 ? 100 : Math.Clamp(Done * 100 / Target, 0, 100);
}

public sealed record AchievementView(string Code, string Title, string Description, string Icon, DateTimeOffset? UnlockedAt)
{
    public bool Unlocked => UnlockedAt is not null;
}

/// <summary>What the client shows after a round or sprint: goal ring, XP, combo, streak and new achievements.</summary>
public sealed record ProgressSnapshot(
    GoalProgress Goal,
    int WeeklyXp,
    int Combo,
    int CurrentStreak,
    int BestStreak,
    int StreakFreezes,
    IReadOnlyList<AchievementView> NewAchievements);

/// <summary>Daily goal, weekly XP, combos, streak protectors and achievements.</summary>
public sealed class EngagementService(WordriseDbContext db, IAppClock clock, StatsService stats, ILogger<EngagementService> logger)
{
    /// <summary>Words finished today in any mode: rounds + words solved in sprints.</summary>
    public async Task<GoalProgress> GetGoalAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        var target = await db.Users.Where(u => u.Id == userId).Select(u => u.DailyGoal).FirstOrDefaultAsync(ct);
        var rounds = await db.Rounds.CountAsync(
            r => r.UserId == userId && r.PlayedOn == today && r.Status != RoundStatus.InProgress, ct);
        var sprintWords = await db.SprintSessions
            .Where(s => s.UserId == userId && s.PlayedOn == today)
            .SumAsync(s => (int?)s.Score, ct) ?? 0;

        return new GoalProgress(Math.Max(1, target), rounds + sprintWords);
    }

    public async Task<int> GetWeeklyXpAsync(int userId, CancellationToken ct)
    {
        var week = WeekRange.For(clock.Today);
        var rounds = await db.Rounds
            .Where(r => r.UserId == userId && r.PlayedOn >= week.Start && r.PlayedOn <= week.End)
            .SumAsync(r => (int?)r.Xp, ct) ?? 0;
        var sprints = await db.SprintSessions
            .Where(s => s.UserId == userId && s.PlayedOn >= week.Start && s.PlayedOn <= week.End)
            .SumAsync(s => (int?)s.Xp, ct) ?? 0;
        return rounds + sprints;
    }

    /// <summary>Consecutive training wins today.</summary>
    public async Task<int> GetComboAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        var results = await db.Rounds
            .Where(r => r.UserId == userId && r.PlayedOn == today && r.Status != RoundStatus.InProgress
                        && (r.Mode == RoundMode.Practice || r.Mode == RoundMode.Listening))
            .OrderByDescending(r => r.FinishedAt).ThenByDescending(r => r.Id)
            .Take(100)
            .Select(r => r.Status == RoundStatus.Won)
            .ToListAsync(ct);
        return ComboCalculator.Current(results);
    }

    /// <summary>
    /// Uses streak protectors for the days missed since the last active day, when there are enough.
    /// Idempotent: once applied, yesterday is an active day and nothing else happens.
    /// </summary>
    public async Task ProtectStreakAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.StreakFreezes <= 0)
        {
            return;
        }

        var today = clock.Today;
        var lastPlayed = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily
                        && r.Status != RoundStatus.InProgress && r.PlayedOn < today)
            .MaxAsync(r => (DateOnly?)r.PlayedOn, ct);
        var lastProtected = await db.StreakFreezeDays
            .Where(f => f.UserId == userId)
            .MaxAsync(f => (DateOnly?)f.Date, ct);
        DateOnly? lastActive = lastProtected is null || (lastPlayed is not null && lastPlayed > lastProtected)
            ? lastPlayed
            : lastProtected;

        var days = StreakFreezePolicy.DaysToProtect(lastActive, today, user.StreakFreezes);
        if (days.Count == 0)
        {
            return;
        }

        db.StreakFreezeDays.AddRange(days.Select(d => new StreakFreezeDay { UserId = userId, Date = d }));
        user.StreakFreezes -= days.Count;

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Streak protected for user {UserId}: {Days} day(s).", userId, days.Count);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request applied the same protection first.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Stores newly earned achievements (unseen) and returns the full catalog with unlock dates.</summary>
    public async Task<IReadOnlyList<AchievementView>> EvaluateAchievementsAsync(int userId, CancellationToken ct)
    {
        var snapshot = await BuildSnapshotAsync(userId, ct);
        var earned = AchievementCatalog.Earned(snapshot);
        var existing = await db.UserAchievements
            .Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.Code, a => a.UnlockedAt, ct);

        var now = clock.UtcNow;
        var added = earned.Where(code => !existing.ContainsKey(code)).ToList();
        if (added.Count > 0)
        {
            db.UserAchievements.AddRange(added.Select(code => new UserAchievement
            {
                UserId = userId,
                Code = code,
                UnlockedAt = now,
                Seen = false
            }));

            try
            {
                await db.SaveChangesAsync(ct);
                foreach (var code in added)
                {
                    existing[code] = now;
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }

        return AchievementCatalog.All
            .Select(a => new AchievementView(a.Code, a.Title, a.Description, a.Icon,
                existing.TryGetValue(a.Code, out var at) ? at : null))
            .ToList();
    }

    /// <summary>Progress after a round/sprint. New achievements are returned once and then marked as seen.</summary>
    public async Task<ProgressSnapshot> GetProgressAsync(int userId, CancellationToken ct)
    {
        await EvaluateAchievementsAsync(userId, ct);

        var unseen = await db.UserAchievements
            .Where(a => a.UserId == userId && !a.Seen)
            .ToListAsync(ct);
        foreach (var a in unseen)
        {
            a.Seen = true;
        }

        if (unseen.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var userStats = await stats.GetStatsAsync(userId, ct);
        var freezes = await db.Users.Where(u => u.Id == userId).Select(u => u.StreakFreezes).FirstOrDefaultAsync(ct);

        return new ProgressSnapshot(
            await GetGoalAsync(userId, ct),
            await GetWeeklyXpAsync(userId, ct),
            await GetComboAsync(userId, ct),
            userStats.CurrentStreak,
            userStats.BestStreak,
            freezes,
            unseen
                .Select(a => AchievementCatalog.Find(a.Code))
                .Where(d => d is not null)
                .Select(d => new AchievementView(d!.Code, d.Title, d.Description, d.Icon, clock.UtcNow))
                .ToList());
    }

    private async Task<AchievementSnapshot> BuildSnapshotAsync(int userId, CancellationToken ct)
    {
        var won = db.Rounds.Where(r => r.UserId == userId && r.Status == RoundStatus.Won);
        var scored = won.Where(r => r.Mode != RoundMode.Listening);
        var userStats = await stats.GetStatsAsync(userId, ct);
        var goal = await GetGoalAsync(userId, ct);

        return new AchievementSnapshot(
            RoundsWon: await scored.CountAsync(ct),
            HasFirstTryWin: await scored.AnyAsync(r => r.AttemptsUsed == 1, ct),
            HasPureSkillWin: await scored.AnyAsync(r => r.AttemptsUsed <= 3 && r.ManualHintsUsed == 0 && !r.EmojiHintUsed, ct),
            BestStreak: userStats.BestStreak,
            WordsLearned: userStats.WordsLearned,
            ReviewWins: await won.CountAsync(r => r.Mode == RoundMode.Review, ct),
            ListeningWins: await won.CountAsync(r => r.Mode == RoundMode.Listening, ct),
            BestSprint: await db.SprintSessions.Where(s => s.UserId == userId).MaxAsync(s => (int?)s.Score, ct) ?? 0,
            DailyGoalMetToday: goal.Met,
            Groups: await db.GroupMembers.CountAsync(m => m.UserId == userId, ct),
            WeeklyXp: await GetWeeklyXpAsync(userId, ct));
    }
}
