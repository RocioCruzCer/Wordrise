using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Services;

namespace Wordrise.Tests;

public class GuessEvaluatorTests
{
    private const LetterState C = LetterState.Correct;
    private const LetterState P = LetterState.Present;
    private const LetterState A = LetterState.Absent;

    [Fact]
    public void Exact_match_is_all_correct()
    {
        Assert.All(GuessEvaluator.Evaluate("bridge", "bridge"), s => Assert.Equal(C, s));
    }

    [Fact]
    public void Wireframe_example_branch_vs_bridge()
    {
        // b, r in the right spot; a, n, c, h not in the word.
        Assert.Equal(new[] { C, C, A, A, A, A }, GuessEvaluator.Evaluate("branch", "bridge"));
    }

    [Fact]
    public void Wireframe_example_border_vs_bridge()
    {
        // b, d in the right spot; r and e in the word; o absent; the second r is absent (only one r in "bridge").
        Assert.Equal(new[] { C, A, P, C, P, A }, GuessEvaluator.Evaluate("border", "bridge"));
    }

    [Fact]
    public void Duplicate_letters_are_limited_by_answer_count()
    {
        // "ensure" has two e's, both matched in place, so the other e's are absent.
        Assert.Equal(new[] { C, A, A, A, A, C }, GuessEvaluator.Evaluate("eeeeee", "ensure"));
    }

    [Fact]
    public void Present_is_not_given_when_letter_already_used_by_correct_match()
    {
        // answer "pocket" has a single 'o' at index 1; guess "oooooo" -> only index 1 correct.
        Assert.Equal(new[] { A, C, A, A, A, A }, GuessEvaluator.Evaluate("oooooo", "pocket"));
    }

    [Fact]
    public void Different_length_throws()
    {
        Assert.Throws<ArgumentException>(() => GuessEvaluator.Evaluate("bridges", "bridge"));
    }

    [Fact]
    public void Encode_decode_round_trip()
    {
        var states = new[] { C, P, A, A, C, P };
        Assert.Equal(states, GuessEvaluator.Decode(GuessEvaluator.Encode(states)));
        Assert.Equal("210021", GuessEvaluator.Encode(states));
    }
}

public class HintPolicyTests
{
    [Theory]
    [InlineData(1, 1)] // part of speech visible from the start (AC-01.1)
    [InlineData(2, 2)] // definition
    [InlineData(3, 2)] // wireframe: attempt 3/6 shows 2 of 4
    [InlineData(4, 3)] // AC-01.3: fourth attempt unlocks the example sentence (3 of 4)
    [InlineData(5, 4)] // first letter
    [InlineData(6, 4)]
    public void Auto_unlocks_by_attempt_number(int nextAttempt, int expected)
    {
        Assert.Equal(expected, HintPolicy.AutoUnlocked(nextAttempt));
    }
}

public class ScoreCalculatorTests
{
    [Theory]
    [InlineData(1, 0, 100)]
    [InlineData(3, 0, 80)]
    [InlineData(3, 1, 70)]
    [InlineData(6, 3, 20)]
    public void Won_rounds_score(int attempts, int manualHints, int expected)
    {
        Assert.Equal(expected, ScoreCalculator.Calculate(true, attempts, manualHints));
    }

    [Fact]
    public void Score_has_a_floor_when_won()
    {
        Assert.Equal(ScoreCalculator.MinWinningScore, ScoreCalculator.Calculate(true, 6, 10));
    }

    [Fact]
    public void Lost_round_scores_zero()
    {
        Assert.Equal(0, ScoreCalculator.Calculate(false, 6, 0));
    }
}

public class DailyWordSelectorTests
{
    [Fact]
    public void Same_date_and_candidates_give_same_word()
    {
        var ids = new[] { 3, 8, 15, 21 };
        var date = new DateOnly(2026, 11, 14);
        Assert.Equal(DailyWordSelector.Select(date, ids), DailyWordSelector.Select(date, ids));
    }

    [Fact]
    public void Consecutive_days_rotate_through_candidates()
    {
        var ids = new[] { 10, 20, 30 };
        var day = new DateOnly(2026, 11, 14);
        var picks = Enumerable.Range(0, 3).Select(i => DailyWordSelector.Select(day.AddDays(i), ids)).ToHashSet();
        Assert.Equal(3, picks.Count);
    }

    [Fact]
    public void Empty_bank_throws()
    {
        Assert.Throws<InvalidOperationException>(() => DailyWordSelector.Select(new DateOnly(2026, 1, 1), []));
    }
}

public class SpacedRepetitionSchedulerTests
{
    private readonly SpacedRepetitionScheduler _scheduler = new([1, 3, 7]);
    private readonly DateOnly _today = new(2026, 11, 14);

    [Fact]
    public void Missed_word_comes_back_tomorrow()
    {
        Assert.Equal((0, _today.AddDays(1)), _scheduler.OnMissed(_today));
    }

    [Fact]
    public void Recalled_word_moves_further_out()
    {
        Assert.Equal((1, _today.AddDays(3)), _scheduler.OnRecalled(0, _today));
        Assert.Equal((2, _today.AddDays(7)), _scheduler.OnRecalled(1, _today));
    }

    [Fact]
    public void Stage_is_capped_at_last_interval()
    {
        Assert.Equal((2, _today.AddDays(7)), _scheduler.OnRecalled(2, _today));
    }

    [Fact]
    public void Invalid_intervals_throw()
    {
        Assert.Throws<ArgumentException>(() => new SpacedRepetitionScheduler([]));
        Assert.Throws<ArgumentException>(() => new SpacedRepetitionScheduler([1, 0]));
    }
}

public class CampCalculatorTests
{
    private readonly CampCalculator _camps = new([
        new Camp("Base", 0), new Camp("Slope", 10), new Camp("Ridge", 30), new Camp("Summit", 100)
    ]);

    [Fact]
    public void Wireframe_example_42_words_is_ridge_with_58_to_summit()
    {
        var p = _camps.For(42);
        Assert.Equal("Ridge", p.Current.Name);
        Assert.Equal("Summit", p.Next!.Name);
        Assert.Equal(58, p.WordsToNext);
        Assert.Equal(42, p.PercentToNext);
    }

    [Fact]
    public void Zero_words_is_base()
    {
        Assert.Equal("Base", _camps.For(0).Current.Name);
    }

    [Fact]
    public void Top_camp_has_no_next()
    {
        var p = _camps.For(150);
        Assert.Equal("Summit", p.Current.Name);
        Assert.Null(p.Next);
        Assert.Equal(0, p.WordsToNext);
    }
}

public class StreakCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static DateOnly[] Days(params int[] offsets) => offsets.Select(o => Today.AddDays(o)).ToArray();

    [Fact]
    public void No_rounds_means_no_streak()
    {
        Assert.Equal(new StreakInfo(0, 0, false), StreakCalculator.Compute([], Today));
    }

    [Fact]
    public void Consecutive_days_including_today()
    {
        Assert.Equal(new StreakInfo(3, 3, true), StreakCalculator.Compute(Days(-2, -1, 0), Today));
    }

    [Fact]
    public void Streak_survives_until_today_is_over()
    {
        // Played yesterday and the day before, not yet today: streak is still 2.
        Assert.Equal(new StreakInfo(2, 2, false), StreakCalculator.Compute(Days(-2, -1), Today));
    }

    [Fact]
    public void Missing_yesterday_breaks_the_streak_but_keeps_best()
    {
        Assert.Equal(new StreakInfo(0, 3, false), StreakCalculator.Compute(Days(-6, -5, -4, -2), Today));
    }

    [Fact]
    public void Duplicates_and_future_dates_are_ignored()
    {
        Assert.Equal(new StreakInfo(1, 1, true), StreakCalculator.Compute(Days(0, 0, 3), Today));
    }
}

public class TrainingWordPickerTests
{
    [Fact]
    public void Avoids_recently_played_words_when_possible()
    {
        var random = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(3, TrainingWordPicker.Pick([1, 2, 3], new HashSet<int> { 1, 2 }, random));
        }
    }

    [Fact]
    public void Falls_back_to_all_candidates_when_everything_was_played()
    {
        var pick = TrainingWordPicker.Pick([1, 2], new HashSet<int> { 1, 2 }, new Random(1));
        Assert.Contains(pick, new[] { 1, 2 });
    }

    [Fact]
    public void Empty_candidates_throw()
    {
        Assert.Throws<InvalidOperationException>(() => TrainingWordPicker.Pick([], new HashSet<int>(), new Random(1)));
    }
}

public class TrainingRequestTests
{
    [Theory]
    [InlineData(null, TrainingSource.Level, CefrLevel.B1)]   // defaults to the user's level
    [InlineData("A2", TrainingSource.Level, CefrLevel.A2)]
    [InlineData("b2", TrainingSource.Level, CefrLevel.B2)]
    [InlineData("past", TrainingSource.PastWords, CefrLevel.B1)]
    public void Parses_valid_tokens(string? token, TrainingSource source, CefrLevel level)
    {
        Assert.True(TrainingRequest.TryParse(RoundMode.Practice, token, CefrLevel.B1, out var request));
        Assert.Equal(source, request.Source);
        Assert.Equal(level, request.Level);
    }

    [Theory]
    [InlineData("C1")]
    [InlineData("99")]
    [InlineData("<script>")]
    public void Rejects_invalid_tokens(string token)
    {
        Assert.False(TrainingRequest.TryParse(RoundMode.Practice, token, CefrLevel.B1, out _));
    }

    [Fact]
    public void Daily_and_review_are_not_training_modes()
    {
        Assert.False(TrainingRequest.TryParse(RoundMode.Daily, "A2", CefrLevel.B1, out _));
        Assert.False(TrainingRequest.TryParse(RoundMode.Review, null, CefrLevel.B1, out _));
    }

    [Fact]
    public void Token_round_trips()
    {
        Assert.Equal("past", new TrainingRequest(RoundMode.Practice, TrainingSource.PastWords, CefrLevel.A2).Token);
        Assert.Equal("B2", new TrainingRequest(RoundMode.Listening, TrainingSource.Level, CefrLevel.B2).Token);
    }
}
