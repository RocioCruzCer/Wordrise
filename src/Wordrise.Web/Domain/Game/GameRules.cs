namespace Wordrise.Web.Domain.Game;

/// <summary>Fixed game rules taken from the spec (HU-01, AC-01.x and wireframes).</summary>
public static class GameRules
{
    public const int MaxAttempts = 6;
    public const int TotalHints = 4;
    public const int MinWordLength = 4;
    public const int MaxWordLength = 10;
}

/// <summary>
/// Two-pass evaluation (Wordle semantics): exact matches first, then "present" letters
/// limited by the remaining count of each letter in the answer.
/// </summary>
public static class GuessEvaluator
{
    public static LetterState[] Evaluate(string guess, string answer)
    {
        ArgumentNullException.ThrowIfNull(guess);
        ArgumentNullException.ThrowIfNull(answer);
        if (guess.Length != answer.Length)
        {
            throw new ArgumentException("Guess and answer must have the same length.", nameof(guess));
        }

        var result = new LetterState[guess.Length];
        var remaining = new Dictionary<char, int>();

        for (var i = 0; i < answer.Length; i++)
        {
            if (guess[i] == answer[i])
            {
                result[i] = LetterState.Correct;
            }
            else
            {
                remaining[answer[i]] = remaining.GetValueOrDefault(answer[i]) + 1;
            }
        }

        for (var i = 0; i < guess.Length; i++)
        {
            if (result[i] == LetterState.Correct)
            {
                continue;
            }

            if (remaining.TryGetValue(guess[i], out var count) && count > 0)
            {
                result[i] = LetterState.Present;
                remaining[guess[i]] = count - 1;
            }
            else
            {
                result[i] = LetterState.Absent;
            }
        }

        return result;
    }

    public static string Encode(IEnumerable<LetterState> states) =>
        string.Concat(states.Select(s => (char)('0' + (int)s)));

    public static LetterState[] Decode(string feedback) =>
        feedback.Select(c => (LetterState)(c - '0')).ToArray();
}

/// <summary>
/// Hint unlocking per wireframe/AC-01.3: hint 1 (part of speech) from the start, hint 2 (definition)
/// at attempt 2, hint 3 (example) at attempt 4, hint 4 (first letter) at attempt 5.
/// </summary>
public static class HintPolicy
{
    private static readonly int[] UnlockAtAttempt = [1, 2, 4, 5];

    /// <param name="nextAttemptNumber">1-based number of the attempt the user is about to make.</param>
    public static int AutoUnlocked(int nextAttemptNumber) =>
        UnlockAtAttempt.Count(threshold => nextAttemptNumber >= threshold);

    public static int UnlockAttemptFor(int hintNumber) => UnlockAtAttempt[hintNumber - 1];
}

/// <summary>
/// ASSUMPTION (not defined in the spec beyond "-10 pts" per hint):
/// won = 100 - 10 per extra attempt - 10 per manual hint, floor 10; lost = 0.
/// </summary>
public static class ScoreCalculator
{
    public const int BaseScore = 100;
    public const int AttemptPenalty = 10;
    public const int HintPenalty = 10;
    public const int MinWinningScore = 10;

    public static int Calculate(bool won, int attemptsUsed, int manualHintsUsed)
    {
        if (!won)
        {
            return 0;
        }

        var score = BaseScore - (AttemptPenalty * (attemptsUsed - 1)) - (HintPenalty * manualHintsUsed);
        return Math.Max(MinWinningScore, score);
    }
}

/// <summary>Random word for training rounds, avoiding the words the user practiced most recently.</summary>
public static class TrainingWordPicker
{
    public static int Pick(IReadOnlyList<int> candidateIds, IReadOnlyCollection<int> recentlyPlayedIds, Random random)
    {
        if (candidateIds.Count == 0)
        {
            throw new InvalidOperationException("There are no words to practice.");
        }

        var recent = recentlyPlayedIds as ISet<int> ?? recentlyPlayedIds.ToHashSet();
        var fresh = candidateIds.Where(id => !recent.Contains(id)).ToList();
        var pool = fresh.Count > 0 ? fresh : candidateIds;
        return pool[random.Next(pool.Count)];
    }
}

/// <summary>Deterministic daily word: same date + level => same word for every user.</summary>
public static class DailyWordSelector
{
    /// <param name="candidateIdsOrdered">Daily-answer word ids for the level, ordered ascending.</param>
    public static int Select(DateOnly date, IReadOnlyList<int> candidateIdsOrdered)
    {
        if (candidateIdsOrdered.Count == 0)
        {
            throw new InvalidOperationException("The word bank has no daily-answer words for this level.");
        }

        return candidateIdsOrdered[date.DayNumber % candidateIdsOrdered.Count];
    }
}
