using Wordrise.Web.Domain;

namespace Wordrise.Web.Services;

public enum GameErrorCode
{
    NotFound,
    RoundFinished,
    InvalidGuess,
    UnknownWord,
    NoHintsLeft,
    Conflict,
    NothingToReview,
    NothingToPractice,
    NotAllowed
}

/// <summary>Where a training round takes its word from.</summary>
public enum TrainingSource
{
    /// <summary>Any word of the chosen level.</summary>
    Level,

    /// <summary>Words from the user's own finished daily rounds (before today).</summary>
    PastWords
}

/// <summary>Parameters of a training round, also used to offer "Next word" with the same settings.</summary>
public sealed record TrainingRequest(RoundMode Mode, TrainingSource Source, CefrLevel Level)
{
    /// <summary>Compact token for URLs: "A2", "B1", "B2" or "past".</summary>
    public string Token => Source == TrainingSource.PastWords ? "past" : Level.ToString();

    public static bool TryParse(RoundMode mode, string? token, CefrLevel fallbackLevel, out TrainingRequest request)
    {
        request = new TrainingRequest(mode, TrainingSource.Level, fallbackLevel);
        if (mode is not (RoundMode.Practice or RoundMode.Listening))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (string.Equals(token, "past", StringComparison.OrdinalIgnoreCase))
        {
            request = request with { Source = TrainingSource.PastWords };
            return true;
        }

        if (Enum.TryParse<CefrLevel>(token, ignoreCase: true, out var level) && Enum.IsDefined(level))
        {
            request = request with { Level = level };
            return true;
        }

        return false;
    }
}

public sealed class GameException(GameErrorCode code, string message) : Exception(message)
{
    public GameErrorCode Code { get; } = code;
}

public sealed record HintView(int Number, string Label, string? Value, int UnlocksAtAttempt);

public sealed record AttemptView(string Guess, IReadOnlyList<LetterState> States);

public sealed record RoundResultView(
    string Word,
    string PartOfSpeech,
    string Definition,
    string Example,
    string Translation,
    CefrLevel Level,
    int Score,
    bool Won);

/// <summary>Round as seen by the client. Never contains the answer while the round is in progress.</summary>
public sealed record RoundState(
    int RoundId,
    RoundMode Mode,
    DateOnly PlayedOn,
    CefrLevel Level,
    int WordLength,
    int AttemptsUsed,
    int MaxAttempts,
    RoundStatus Status,
    int HintsUnlocked,
    int TotalHints,
    int ManualHintsUsed,
    IReadOnlyList<HintView> Hints,
    IReadOnlyList<AttemptView> Attempts,
    RoundResultView? Result,
    string? SpokenWord,
    bool EmojiAvailable,
    bool EmojiHintUsed,
    string? Emoji,
    int Xp);

public sealed record ReviewItem(int WordId, string Text, CefrLevel Level, DateOnly LastMissedOn);

public sealed record NotebookItem(string Text, CefrLevel Level, string Definition, string Example, string Translation);

public sealed record DayAttempts(DateOnly Date, int? Attempts, bool IsToday);

public sealed record UserStats(
    int WordsLearned,
    int AccuracyPercent,
    double AverageAttempts,
    int ToReview,
    int RoundsFinished,
    int CurrentStreak,
    int BestStreak);
