namespace Wordrise.Web.Domain;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Stored normalized (trimmed, lower-case). Unique.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public CefrLevel Level { get; set; }
    public bool PronunciationAudioEnabled { get; set; } = true;

    /// <summary>Words per day the user wants to finish (any mode).</summary>
    public int DailyGoal { get; set; } = 3;

    /// <summary>Streak protectors available (0..2). Earned every 7-day streak, used automatically.</summary>
    public int StreakFreezes { get; set; }

    public bool SoundEnabled { get; set; } = true;

    /// <summary>Show the user's first name in the public level leagues.</summary>
    public bool ShowInLeagues { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Round> Rounds { get; set; } = new List<Round>();
    public ICollection<PendingReview> PendingReviews { get; set; } = new List<PendingReview>();
}

public class Word
{
    public int Id { get; set; }

    /// <summary>Lower-case ASCII letters only. Unique.</summary>
    public string Text { get; set; } = string.Empty;

    public CefrLevel Level { get; set; }
    public PartOfSpeech PartOfSpeech { get; set; }
    public string DefinitionEn { get; set; } = string.Empty;

    /// <summary>Example sentence with the target word replaced by "_____".</summary>
    public string ExampleSentence { get; set; } = string.Empty;

    public string TranslationEs { get; set; } = string.Empty;

    /// <summary>True when the word can be chosen as a daily answer.</summary>
    public bool IsDailyAnswer { get; set; } = true;

    /// <summary>Optional picture hint for concrete words (an emoji, rendered by the device font).</summary>
    public string? Emoji { get; set; }
}

public class DailyWord
{
    public int Id { get; set; }

    /// <summary>Calendar date in the application's time zone (America/Mexico_City).</summary>
    public DateOnly Date { get; set; }

    public CefrLevel Level { get; set; }
    public int WordId { get; set; }
    public Word Word { get; set; } = null!;
}

public class Round
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public RoundMode Mode { get; set; }

    /// <summary>Set for daily rounds; null for review rounds.</summary>
    public int? DailyWordId { get; set; }
    public DailyWord? DailyWord { get; set; }

    /// <summary>Target word (denormalized from DailyWord for daily rounds so review rounds share the same shape).</summary>
    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    /// <summary>Calendar date (app time zone) the round was started.</summary>
    public DateOnly PlayedOn { get; set; }

    public RoundStatus Status { get; set; } = RoundStatus.InProgress;
    public int AttemptsUsed { get; set; }

    /// <summary>Hints visible to the user (1..4).</summary>
    public int HintsUnlocked { get; set; } = 1;

    /// <summary>Hints unlocked early with "Use a hint" (score penalty).</summary>
    public int ManualHintsUsed { get; set; }

    public int Score { get; set; }

    /// <summary>Experience points earned when the round finished (feeds the weekly leagues).</summary>
    public int Xp { get; set; }

    public bool EmojiHintUsed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public ICollection<Attempt> Attempts { get; set; } = new List<Attempt>();
}

public class Attempt
{
    public long Id { get; set; }
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;

    /// <summary>1-based attempt number inside the round.</summary>
    public int Number { get; set; }

    public string GuessText { get; set; } = string.Empty;

    /// <summary>Per-letter states encoded as digits (0 absent, 1 present, 2 correct), e.g. "220010".</summary>
    public string Feedback { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public class PendingReview
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    /// <summary>Index into the configured interval list (0 = first interval).</summary>
    public int Stage { get; set; }

    public DateOnly NextReviewOn { get; set; }
    public DateOnly LastMissedOn { get; set; }
}

/// <summary>A past day covered by a streak protector.</summary>
public class StreakFreezeDay
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
}

public class UserAchievement
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset UnlockedAt { get; set; }

    /// <summary>False until the client has shown the "achievement unlocked" notice.</summary>
    public bool Seen { get; set; }
}

/// <summary>60-second anagram sprint. Validated on the server; the client only sees scrambled letters.</summary>
public class SprintSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public CefrLevel Level { get; set; }
    public DateOnly PlayedOn { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int Score { get; set; }
    public int Skipped { get; set; }
    public int Xp { get; set; }

    /// <summary>Increments with every new word; the client echoes it to make answers idempotent.</summary>
    public int ChallengeNumber { get; set; }

    public int? CurrentWordId { get; set; }
    public Word? CurrentWord { get; set; }

    /// <summary>Scrambled letters of the current word, stored so a reload shows the same puzzle.</summary>
    public string CurrentLetters { get; set; } = string.Empty;

    public List<int> SeenWordIds { get; set; } = new();
    public List<int> SolvedWordIds { get; set; } = new();
}

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Join code shared by the group (6 characters, no ambiguous letters).</summary>
    public string Code { get; set; } = string.Empty;

    public int? OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}

public class GroupMember
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset JoinedAt { get; set; }
}
