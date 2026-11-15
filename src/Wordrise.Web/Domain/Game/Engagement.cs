using System.Security.Cryptography;

namespace Wordrise.Web.Domain.Game;

/// <summary>
/// Experience points per finished round. ASSUMPTION (design decision of increment 4):
/// - Daily, review and practice: the round score when won, 5 when lost; the daily challenge adds a +25 bonus.
/// - Listening: fixed 10 / 2, because the client receives the word to speak it (easy to farm otherwise).
/// - Sprint: 5 per solved word.
/// </summary>
public static class XpCalculator
{
    public const int LostRoundXp = 5;
    public const int DailyBonusXp = 25;
    public const int ListeningWonXp = 10;
    public const int ListeningLostXp = 2;
    public const int SprintXpPerWord = 5;

    public static int ForRound(RoundMode mode, bool won, int score) => mode switch
    {
        RoundMode.Listening => won ? ListeningWonXp : ListeningLostXp,
        RoundMode.Daily => (won ? score : LostRoundXp) + DailyBonusXp,
        _ => won ? score : LostRoundXp
    };

    public static int ForSprint(int solvedWords) => Math.Max(0, solvedWords) * SprintXpPerWord;
}

/// <summary>League week: Monday to Sunday in the app time zone.</summary>
public readonly record struct WeekRange(DateOnly Start, DateOnly End)
{
    public static WeekRange For(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        var start = day.AddDays(-offset);
        return new WeekRange(start, start.AddDays(6));
    }

    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

/// <summary>Streak protector rules (proposal of increment 4).</summary>
public static class StreakFreezePolicy
{
    public const int MaxFreezes = 2;
    public const int EarnEveryDays = 7;

    /// <summary>A protector is earned each time the current streak reaches a multiple of 7.</summary>
    public static int AfterDailyFinished(int freezes, int currentStreak) =>
        currentStreak > 0 && currentStreak % EarnEveryDays == 0 ? Math.Min(MaxFreezes, freezes + 1) : freezes;

    /// <summary>
    /// Days that protectors should cover so the streak survives: every missed day between the last active day
    /// (played or protected) and yesterday, only if there are enough protectors for all of them.
    /// </summary>
    public static IReadOnlyList<DateOnly> DaysToProtect(DateOnly? lastActiveDay, DateOnly today, int freezes)
    {
        if (lastActiveDay is null || freezes <= 0)
        {
            return [];
        }

        var yesterday = today.AddDays(-1);
        var missed = yesterday.DayNumber - lastActiveDay.Value.DayNumber;
        if (missed < 1 || missed > freezes)
        {
            return [];
        }

        return Enumerable.Range(1, missed).Select(i => lastActiveDay.Value.AddDays(i)).ToList();
    }
}

/// <summary>Consecutive training wins today (shown as "Combo xN").</summary>
public static class ComboCalculator
{
    /// <param name="resultsNewestFirst">Won/lost of today's finished training rounds, newest first.</param>
    public static int Current(IEnumerable<bool> resultsNewestFirst) => resultsNewestFirst.TakeWhile(won => won).Count();
}

public sealed record AchievementDefinition(string Code, string Title, string Description, string Icon);

/// <summary>Numbers the achievement rules need; built by EngagementService from stored data.</summary>
public sealed record AchievementSnapshot(
    int RoundsWon,
    bool HasFirstTryWin,
    bool HasPureSkillWin,
    int BestStreak,
    int WordsLearned,
    int ReviewWins,
    int ListeningWins,
    int BestSprint,
    bool DailyGoalMetToday,
    int Groups,
    int WeeklyXp);

public static class AchievementCatalog
{
    public static readonly IReadOnlyList<AchievementDefinition> All =
    [
        new("first_win", "First peak", "Win your first round.", "🏔️"),
        new("first_try", "Bullseye", "Guess a word on the first attempt.", "🎯"),
        new("pure_skill", "Pure skill", "Win in 3 attempts or fewer without extra hints.", "🧠"),
        new("streak_3", "Warming up", "Reach a 3-day streak.", "🔥"),
        new("streak_7", "On fire", "Reach a 7-day streak.", "☄️"),
        new("streak_30", "Unstoppable", "Reach a 30-day streak.", "🌋"),
        new("learned_10", "Collector", "Learn 10 words.", "📚"),
        new("learned_50", "Word hoarder", "Learn 50 words.", "🗃️"),
        new("learned_100", "Lexicon", "Learn 100 words.", "🏛️"),
        new("reviewer_10", "Never forget", "Win 10 review rounds.", "🔁"),
        new("listener_10", "Sharp ears", "Win 10 listening challenges.", "🎧"),
        new("sprint_10", "Sprinter", "Solve 10 words in one sprint.", "⚡"),
        new("goal_met", "Goal getter", "Reach your daily goal.", "✅"),
        new("team_player", "Team player", "Create or join a group.", "🤝"),
        new("weekly_500", "Rising star", "Earn 500 XP in one week.", "⭐")
    ];

    public static AchievementDefinition? Find(string code) => All.FirstOrDefault(a => a.Code == code);

    /// <summary>Codes whose condition is met by the snapshot (already unlocked ones included).</summary>
    public static IReadOnlyList<string> Earned(AchievementSnapshot s)
    {
        var earned = new List<string>();
        void Add(bool condition, string code)
        {
            if (condition)
            {
                earned.Add(code);
            }
        }

        Add(s.RoundsWon >= 1, "first_win");
        Add(s.HasFirstTryWin, "first_try");
        Add(s.HasPureSkillWin, "pure_skill");
        Add(s.BestStreak >= 3, "streak_3");
        Add(s.BestStreak >= 7, "streak_7");
        Add(s.BestStreak >= 30, "streak_30");
        Add(s.WordsLearned >= 10, "learned_10");
        Add(s.WordsLearned >= 50, "learned_50");
        Add(s.WordsLearned >= 100, "learned_100");
        Add(s.ReviewWins >= 10, "reviewer_10");
        Add(s.ListeningWins >= 10, "listener_10");
        Add(s.BestSprint >= 10, "sprint_10");
        Add(s.DailyGoalMetToday, "goal_met");
        Add(s.Groups >= 1, "team_player");
        Add(s.WeeklyXp >= 500, "weekly_500");
        return earned;
    }
}

/// <summary>60-second anagram sprint rules.</summary>
public static class SprintRules
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);

    /// <summary>Tolerance for network latency on the last answer.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    public static string Scramble(string word, Random random)
    {
        if (word.Length < 2 || word.Distinct().Count() < 2)
        {
            return word;
        }

        var letters = word.ToCharArray();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            random.Shuffle(letters);
            var candidate = new string(letters);
            if (candidate != word)
            {
                return candidate;
            }
        }

        // Guaranteed different: rotate by one.
        return word[1..] + word[0];
    }

    /// <summary>Same letters, same counts (order ignored).</summary>
    public static bool IsAnagram(string a, string b) =>
        a.Length == b.Length && a.Order().SequenceEqual(b.Order());
}

/// <summary>Join codes: 6 characters without look-alike letters (no 0/O, 1/I/L).</summary>
public static class GroupCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        return normalized.Length == Length && normalized.All(c => Alphabet.Contains(c)) ? normalized : null;
    }
}

public static class DisplayName
{
    /// <summary>Public name in leagues: first word of the name only (privacy).</summary>
    public static string ForLeague(string fullName)
    {
        var first = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Climber";
        return first.Length > 20 ? first[..20] : first;
    }
}
