namespace Wordrise.Web.Domain.Game;

/// <summary>
/// Spaced repetition (HU-04). ASSUMPTION: the spec does not define intervals; they come from
/// configuration ("Review:IntervalsInDays"), default 1, 3, 7, 14, 30.
/// </summary>
public sealed class SpacedRepetitionScheduler
{
    private readonly int[] _intervals;

    public SpacedRepetitionScheduler(IReadOnlyList<int> intervalsInDays)
    {
        if (intervalsInDays.Count == 0 || intervalsInDays.Any(d => d <= 0))
        {
            throw new ArgumentException("Intervals must be a non-empty list of positive integers.", nameof(intervalsInDays));
        }

        _intervals = intervalsInDays.ToArray();
    }

    /// <summary>Word missed (daily or review): restart at the first interval.</summary>
    public (int Stage, DateOnly NextReviewOn) OnMissed(DateOnly today) => (0, today.AddDays(_intervals[0]));

    /// <summary>Review word guessed: move to the next (longer) interval, capped at the last one.</summary>
    public (int Stage, DateOnly NextReviewOn) OnRecalled(int currentStage, DateOnly today)
    {
        var next = Math.Min(currentStage + 1, _intervals.Length - 1);
        return (next, today.AddDays(_intervals[next]));
    }
}

public sealed record Camp(string Name, int MinWordsLearned);

public sealed record CampProgress(Camp Current, Camp? Next, int WordsLearned)
{
    public int WordsToNext => Next is null ? 0 : Math.Max(0, Next.MinWordsLearned - WordsLearned);

    /// <summary>0..100 progress toward the next camp's threshold.</summary>
    public int PercentToNext => Next is null || Next.MinWordsLearned == 0
        ? 100
        : Math.Clamp(WordsLearned * 100 / Next.MinWordsLearned, 0, 100);
}

/// <summary>
/// Camps Base → Slope → Ridge → Summit (proposal §5.2). ASSUMPTION: word thresholds are
/// configurable ("Camps"); the spec only fixes the names and that progress is measured in words learned.
/// </summary>
public sealed class CampCalculator
{
    private readonly Camp[] _camps;

    public CampCalculator(IEnumerable<Camp> camps)
    {
        _camps = camps.OrderBy(c => c.MinWordsLearned).ToArray();
        if (_camps.Length == 0 || _camps[0].MinWordsLearned != 0)
        {
            throw new ArgumentException("Camps must start with a camp at 0 words.", nameof(camps));
        }
    }

    public CampProgress For(int wordsLearned)
    {
        var index = Array.FindLastIndex(_camps, c => wordsLearned >= c.MinWordsLearned);
        var next = index + 1 < _camps.Length ? _camps[index + 1] : null;
        return new CampProgress(_camps[index], next, wordsLearned);
    }
}

public sealed record StreakInfo(int Current, int Best, bool PlayedToday);

/// <summary>
/// Daily streak. ASSUMPTION (not in the spec): a day counts when the daily round was finished (won or lost),
/// because the streak rewards the habit. The current streak stays alive until the end of today, so it
/// is counted up to yesterday when today's word has not been finished yet.
/// Days covered by a streak protector keep the streak alive but do not add to it.
/// </summary>
public static class StreakCalculator
{
    public static StreakInfo Compute(IEnumerable<DateOnly> playedDays, DateOnly today) =>
        Compute(playedDays, [], today);

    public static StreakInfo Compute(IEnumerable<DateOnly> playedDays, IEnumerable<DateOnly> protectedDays, DateOnly today)
    {
        var played = playedDays.Where(d => d <= today).ToHashSet();
        var active = played.Union(protectedDays.Where(d => d < today)).OrderBy(d => d).ToList();
        if (played.Count == 0)
        {
            return new StreakInfo(0, 0, false);
        }

        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in active)
        {
            if (previous is not null && day.DayNumber - previous.Value.DayNumber != 1)
            {
                run = 0;
            }

            if (played.Contains(day))
            {
                run++;
            }

            best = Math.Max(best, run);
            previous = day;
        }

        var last = active[^1];
        var playedToday = played.Contains(today);
        var current = last == today || last == today.AddDays(-1) ? run : 0;

        return new StreakInfo(current, best, playedToday);
    }
}
