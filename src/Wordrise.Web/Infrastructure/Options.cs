namespace Wordrise.Web.Infrastructure;

public sealed class GameOptions
{
    public const string Section = "Game";

    /// <summary>IANA time zone that defines when the day changes (proposal §3.5).</summary>
    public string TimeZone { get; set; } = "America/Mexico_City";

    /// <summary>Spaced repetition intervals in days. ASSUMPTION: not defined in the spec.</summary>
    public int[] ReviewIntervalsInDays { get; set; } = [1, 3, 7, 14, 30];

    /// <summary>Camp thresholds (words learned). ASSUMPTION: names from the spec, thresholds pending definition.</summary>
    public CampOption[] Camps { get; set; } =
    [
        new() { Name = "Base", MinWordsLearned = 0 },
        new() { Name = "Slope", MinWordsLearned = 10 },
        new() { Name = "Ridge", MinWordsLearned = 30 },
        new() { Name = "Summit", MinWordsLearned = 100 }
    ];
}

public sealed class CampOption
{
    public string Name { get; set; } = string.Empty;
    public int MinWordsLearned { get; set; }
}
