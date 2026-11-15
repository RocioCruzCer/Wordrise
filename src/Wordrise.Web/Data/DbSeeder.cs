using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;

namespace Wordrise.Web.Data;

/// <summary>
/// Inserts the initial word bank from Data/Seed/words.json. Idempotent: words that already exist
/// (matched by text) are left untouched, so edits made in the database are never overwritten.
/// </summary>
public sealed class DbSeeder(WordriseDbContext db, IWebHostEnvironment env, ILogger<DbSeeder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SeedWordsAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "Seed", "words.json");
        await using var stream = File.OpenRead(path);
        var seedWords = await JsonSerializer.DeserializeAsync<List<SeedWord>>(stream, JsonOptions, ct)
                        ?? throw new InvalidOperationException("words.json is empty.");

        var existing = await db.Words.AsNoTracking().Select(w => w.Text).ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        // Picture hints added after a word was seeded: fill only where the database has none (never overwrite edits).
        var emojiByText = seedWords
            .Where(s => !string.IsNullOrWhiteSpace(s.Emoji))
            .ToDictionary(s => s.Text.Trim().ToLowerInvariant(), s => s.Emoji!.Trim());
        var missingEmoji = await db.Words.Where(w => w.Emoji == null).ToListAsync(ct);
        var emojiUpdates = 0;
        foreach (var word in missingEmoji)
        {
            if (emojiByText.TryGetValue(word.Text, out var emoji))
            {
                word.Emoji = emoji;
                emojiUpdates++;
            }
        }

        var toInsert = new List<Word>();
        foreach (var s in seedWords)
        {
            var text = s.Text.Trim().ToLowerInvariant();
            if (text.Length is < GameRules.MinWordLength or > GameRules.MaxWordLength || !text.All(char.IsAsciiLetterLower))
            {
                throw new InvalidOperationException($"Invalid seed word '{s.Text}'.");
            }

            if (existingSet.Add(text))
            {
                toInsert.Add(new Word
                {
                    Text = text,
                    Level = s.Level,
                    PartOfSpeech = s.PartOfSpeech,
                    DefinitionEn = s.DefinitionEn,
                    ExampleSentence = s.ExampleSentence,
                    TranslationEs = s.TranslationEs,
                    IsDailyAnswer = s.IsDailyAnswer ?? true,
                    Emoji = string.IsNullOrWhiteSpace(s.Emoji) ? null : s.Emoji.Trim()
                });
            }
        }

        if (toInsert.Count > 0 || emojiUpdates > 0)
        {
            db.Words.AddRange(toInsert);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Word bank seed: {Inserted} inserted, {Emoji} picture hints added, {Total} in file.",
            toInsert.Count, emojiUpdates, seedWords.Count);
    }

    private sealed record SeedWord(
        string Text,
        CefrLevel Level,
        PartOfSpeech PartOfSpeech,
        string DefinitionEn,
        string ExampleSentence,
        string TranslationEs,
        bool? IsDailyAnswer,
        string? Emoji);
}
