namespace Wordrise.Web.Domain;

/// <summary>CEFR levels supported in v1 (C1/C2 are out of scope).</summary>
public enum CefrLevel
{
    A2 = 1,
    B1 = 2,
    B2 = 3
}

public enum PartOfSpeech
{
    Noun = 1,
    Verb = 2,
    Adjective = 3,
    Adverb = 4,
    Preposition = 5,
    Conjunction = 6,
    Pronoun = 7
}

public enum RoundMode
{
    /// <summary>One shared word per day and level; counts for the streak.</summary>
    Daily = 1,

    /// <summary>Spaced-repetition review of a missed word.</summary>
    Review = 2,

    /// <summary>Unlimited training with a random word (by level or from the user's past daily words).</summary>
    Practice = 3,

    /// <summary>Unlimited training where the word is heard first. The client receives the word to speak it.</summary>
    Listening = 4
}

public enum RoundStatus
{
    InProgress = 1,
    Won = 2,
    Lost = 3
}

/// <summary>Per-letter feedback for a guess.</summary>
public enum LetterState
{
    Absent = 0,
    Present = 1,
    Correct = 2
}
