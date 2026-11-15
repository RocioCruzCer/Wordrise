using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;

namespace Wordrise.Tests;

public class XpCalculatorTests
{
    [Theory]
    [InlineData(RoundMode.Daily, true, 80, 105)]   // score + daily bonus
    [InlineData(RoundMode.Daily, false, 0, 30)]    // participation + daily bonus
    [InlineData(RoundMode.Practice, true, 70, 70)]
    [InlineData(RoundMode.Review, false, 0, 5)]
    [InlineData(RoundMode.Listening, true, 100, 10)]
    [InlineData(RoundMode.Listening, false, 0, 2)]
    public void Round_xp(RoundMode mode, bool won, int score, int expected)
    {
        Assert.Equal(expected, XpCalculator.ForRound(mode, won, score));
    }

    [Fact]
    public void Sprint_xp_is_per_solved_word()
    {
        Assert.Equal(40, XpCalculator.ForSprint(8));
        Assert.Equal(0, XpCalculator.ForSprint(-1));
    }
}

public class WeekRangeTests
{
    [Theory]
    [InlineData(2026, 11, 16, 2026, 11, 16)] // Monday
    [InlineData(2026, 11, 14, 2026, 11, 9)]  // Saturday
    [InlineData(2026, 11, 15, 2026, 11, 9)]  // Sunday belongs to the previous Monday's week
    public void Week_starts_on_monday(int y, int m, int d, int sy, int sm, int sd)
    {
        var week = WeekRange.For(new DateOnly(y, m, d));
        Assert.Equal(new DateOnly(sy, sm, sd), week.Start);
        Assert.Equal(week.Start.AddDays(6), week.End);
        Assert.True(week.Contains(new DateOnly(y, m, d)));
    }
}

public class StreakFreezeTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    [Theory]
    [InlineData(0, 7, 1)]
    [InlineData(1, 14, 2)]
    [InlineData(2, 21, 2)] // capped
    [InlineData(0, 6, 0)]
    [InlineData(0, 0, 0)]
    public void Earned_every_seven_days(int freezes, int streak, int expected)
    {
        Assert.Equal(expected, StreakFreezePolicy.AfterDailyFinished(freezes, streak));
    }

    [Fact]
    public void Protects_one_missed_day()
    {
        var days = StreakFreezePolicy.DaysToProtect(Today.AddDays(-2), Today, freezes: 1);
        Assert.Equal(new[] { Today.AddDays(-1) }, days);
    }

    [Fact]
    public void Does_nothing_when_not_enough_protectors()
    {
        Assert.Empty(StreakFreezePolicy.DaysToProtect(Today.AddDays(-4), Today, freezes: 2));
    }

    [Fact]
    public void Does_nothing_when_yesterday_was_played()
    {
        Assert.Empty(StreakFreezePolicy.DaysToProtect(Today.AddDays(-1), Today, freezes: 2));
        Assert.Empty(StreakFreezePolicy.DaysToProtect(null, Today, freezes: 2));
    }

    [Fact]
    public void Protected_days_keep_the_streak_without_adding_to_it()
    {
        // Played -3 and -2, protected -1, not yet today: streak 2 is alive.
        var streak = StreakCalculator.Compute([Today.AddDays(-3), Today.AddDays(-2)], [Today.AddDays(-1)], Today);
        Assert.Equal(new StreakInfo(2, 2, false), streak);

        // Then playing today continues it to 3.
        streak = StreakCalculator.Compute([Today.AddDays(-3), Today.AddDays(-2), Today], [Today.AddDays(-1)], Today);
        Assert.Equal(new StreakInfo(3, 3, true), streak);
    }
}

public class ComboCalculatorTests
{
    [Fact]
    public void Counts_wins_until_the_first_loss()
    {
        Assert.Equal(3, ComboCalculator.Current([true, true, true, false, true]));
        Assert.Equal(0, ComboCalculator.Current([false, true]));
        Assert.Equal(0, ComboCalculator.Current([]));
    }
}

public class AchievementCatalogTests
{
    private static AchievementSnapshot Empty => new(0, false, false, 0, 0, 0, 0, 0, false, 0, 0);

    [Fact]
    public void Nothing_earned_at_the_start()
    {
        Assert.Empty(AchievementCatalog.Earned(Empty));
    }

    [Fact]
    public void Thresholds_unlock_the_right_codes()
    {
        var snapshot = Empty with { RoundsWon = 1, BestStreak = 7, WordsLearned = 12, BestSprint = 10, Groups = 1 };
        var earned = AchievementCatalog.Earned(snapshot);

        Assert.Contains("first_win", earned);
        Assert.Contains("streak_3", earned);
        Assert.Contains("streak_7", earned);
        Assert.DoesNotContain("streak_30", earned);
        Assert.Contains("learned_10", earned);
        Assert.DoesNotContain("learned_50", earned);
        Assert.Contains("sprint_10", earned);
        Assert.Contains("team_player", earned);
    }

    [Fact]
    public void Every_code_is_unique_and_described()
    {
        Assert.Equal(AchievementCatalog.All.Count, AchievementCatalog.All.Select(a => a.Code).Distinct().Count());
        Assert.All(AchievementCatalog.All, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.False(string.IsNullOrWhiteSpace(a.Icon));
            Assert.True(a.Code.Length <= 40);
        });
    }
}

public class SprintRulesTests
{
    [Fact]
    public void Scramble_keeps_letters_and_changes_order()
    {
        var random = new Random(7);
        foreach (var word in new[] { "bridge", "apple", "resilient", "agree" })
        {
            var scrambled = SprintRules.Scramble(word, random);
            Assert.NotEqual(word, scrambled);
            Assert.True(SprintRules.IsAnagram(word, scrambled));
        }
    }

    [Fact]
    public void Single_letter_repeats_cannot_be_scrambled()
    {
        Assert.Equal("aaaa", SprintRules.Scramble("aaaa", new Random(1)));
    }

    [Fact]
    public void Anagram_check()
    {
        Assert.True(SprintRules.IsAnagram("silent", "listen"));
        Assert.False(SprintRules.IsAnagram("silent", "lister"));
        Assert.False(SprintRules.IsAnagram("abc", "abcd"));
    }
}

public class GroupCodeTests
{
    [Fact]
    public void New_codes_use_the_safe_alphabet()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = GroupCode.New();
            Assert.Equal(GroupCode.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, GroupCode.Alphabet));
        }
    }

    [Theory]
    [InlineData(" ab3k9z ", "AB3K9Z")]
    [InlineData("AB3K9", null)]     // too short
    [InlineData("AB0K9Z", null)]    // 0 is not in the alphabet
    [InlineData(null, null)]
    public void Normalizes_user_input(string? input, string? expected)
    {
        Assert.Equal(expected, GroupCode.Normalize(input));
    }
}

public class DisplayNameTests
{
    [Theory]
    [InlineData("Joseph Abrahan Yáñez", "Joseph")]
    [InlineData("  Rocio ", "Rocio")]
    [InlineData("", "Climber")]
    public void Uses_first_name_only(string input, string expected)
    {
        Assert.Equal(expected, DisplayName.ForLeague(input));
    }
}
