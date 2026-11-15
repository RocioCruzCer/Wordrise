using System.Collections.Frozen;
using Wordrise.Web.Domain.Game;

namespace Wordrise.Web.Services;

/// <summary>
/// Words accepted as attempts (proposal §2.3: separate from the daily-answer bank).
/// Loaded once from Data/Seed/valid-guesses.txt into an immutable set (~100k entries).
/// </summary>
public interface IValidGuessList
{
    bool Contains(string word);
}

public sealed class ValidGuessList : IValidGuessList
{
    private readonly FrozenSet<string> _words;

    public ValidGuessList(IEnumerable<string> words)
    {
        _words = words
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length is >= GameRules.MinWordLength and <= GameRules.MaxWordLength && w.All(char.IsAsciiLetterLower))
            .ToFrozenSet(StringComparer.Ordinal);
    }

    public int Count => _words.Count;

    public bool Contains(string word) => _words.Contains(word);

    public static ValidGuessList LoadFromFile(string path) => new(File.ReadLines(path));
}
