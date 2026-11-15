using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;

namespace Wordrise.Tests;

public class AppClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Day_changes_at_midnight_in_mexico_city_not_utc()
    {
        // 2026-11-15 03:00 UTC is still 2026-11-14 21:00 in Mexico City (UTC-6, no DST since 2022).
        var clock = new AppClock(
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.Zero)),
            Options.Create(new GameOptions()));

        Assert.Equal(new DateOnly(2026, 11, 14), clock.Today);
    }

    [Fact]
    public void Next_word_arrives_at_local_midnight()
    {
        // 2026-11-15 03:00 UTC = 21:00 in Mexico City -> 3 hours to midnight.
        var clock = new AppClock(
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.Zero)),
            Options.Create(new GameOptions()));

        Assert.Equal(TimeSpan.FromHours(3), clock.UntilNextDay);
    }

    [Fact]
    public void Unknown_time_zone_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => AppClock.ResolveTimeZone("Mars/Olympus_Mons"));
    }
}

public class ConnectionStringResolverTests
{
    [Fact]
    public void Converts_render_database_url()
    {
        var cs = ConnectionStringResolver.FromUrl("postgres://wr_user:p%40ss@dpg-abc.oregon-postgres.render.com:5433/wordrise");
        var b = new NpgsqlConnectionStringBuilder(cs);

        Assert.Equal("dpg-abc.oregon-postgres.render.com", b.Host);
        Assert.Equal(5433, b.Port);
        Assert.Equal("wordrise", b.Database);
        Assert.Equal("wr_user", b.Username);
        Assert.Equal("p@ss", b.Password);
    }

    [Fact]
    public void Defaults_port_to_5432()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStringResolver.FromUrl("postgresql://u:p@host/db"));
        Assert.Equal(5432, b.Port);
    }

    [Fact]
    public void Rejects_other_schemes()
    {
        Assert.Throws<InvalidOperationException>(() => ConnectionStringResolver.FromUrl("mysql://u:p@host/db"));
    }
}

public class NamingTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("DefinitionEn", "definition_en")]
    [InlineData("IsDailyAnswer", "is_daily_answer")]
    [InlineData("DailyWordId", "daily_word_id")]
    public void Converts_to_snake_case(string input, string expected)
    {
        Assert.Equal(expected, WordriseDbContext.ToSnakeCase(input));
    }
}

public class ValidGuessListTests
{
    [Fact]
    public void Normalizes_and_filters_entries()
    {
        var list = new ValidGuessList(["Bridge ", "ab", "naïve", "border", "toolongwordxx"]);

        Assert.True(list.Contains("bridge"));
        Assert.True(list.Contains("border"));
        Assert.False(list.Contains("ab"));
        Assert.False(list.Contains("toolongwordxx"));
        Assert.Equal(2, list.Count);
    }
}

public class RoundStateTests
{
    private static Round NewRound(RoundStatus status) => new()
    {
        Id = 7,
        Mode = RoundMode.Daily,
        Status = status,
        AttemptsUsed = 2,
        HintsUnlocked = 2,
        Word = new Word
        {
            Text = "bridge",
            Level = CefrLevel.B1,
            PartOfSpeech = PartOfSpeech.Noun,
            DefinitionEn = "a structure that connects two sides of a river or road",
            ExampleSentence = "We walked across the old _____.",
            TranslationEs = "puente"
        }
    };

    [Fact]
    public void In_progress_state_never_contains_the_answer()
    {
        var state = GameService.ToState(NewRound(RoundStatus.InProgress));
        var json = JsonSerializer.Serialize(state);

        Assert.Null(state.Result);
        Assert.DoesNotContain("bridge", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puente", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(state.Hints[2].Value); // example locked
        Assert.Null(state.Hints[3].Value); // first letter locked
    }

    [Fact]
    public void Only_listening_rounds_send_the_word_to_speak()
    {
        var listening = NewRound(RoundStatus.InProgress);
        listening.Mode = RoundMode.Listening;
        Assert.Equal("bridge", GameService.ToState(listening).SpokenWord);

        foreach (var mode in new[] { RoundMode.Daily, RoundMode.Review, RoundMode.Practice })
        {
            var round = NewRound(RoundStatus.InProgress);
            round.Mode = mode;
            Assert.Null(GameService.ToState(round).SpokenWord);
        }
    }

    [Fact]
    public void Finished_state_reveals_word_with_filled_example()
    {
        var state = GameService.ToState(NewRound(RoundStatus.Lost));

        Assert.NotNull(state.Result);
        Assert.Equal("bridge", state.Result!.Word);
        Assert.Equal("We walked across the old bridge.", state.Result.Example);
        Assert.Equal("puente", state.Result.Translation);
    }
}

public class SeedDataTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wordrise.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Fact]
    public void Word_bank_entries_are_well_formed()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "Wordrise.Web", "Data", "Seed", "words.json")));
        var texts = new HashSet<string>();

        foreach (var w in doc.RootElement.EnumerateArray())
        {
            var text = w.GetProperty("text").GetString()!;
            var isDailyAnswer = !w.TryGetProperty("isDailyAnswer", out var daily) || daily.GetBoolean();
            // AC-01.1: the daily grid has 6 letters; training words may be 4-10 letters.
            Assert.Matches(isDailyAnswer ? "^[a-z]{6}$" : "^[a-z]{4,10}$", text);
            Assert.True(texts.Add(text), $"Duplicate word '{text}'.");
            Assert.Contains(w.GetProperty("level").GetString(), new[] { "A2", "B1", "B2" });
            Assert.True(Enum.TryParse<PartOfSpeech>(w.GetProperty("partOfSpeech").GetString(), out _));
            Assert.Contains("_____", w.GetProperty("exampleSentence").GetString());
            Assert.DoesNotContain(text, w.GetProperty("definitionEn").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(w.GetProperty("translationEs").GetString()));
        }

        foreach (var level in new[] { "A2", "B1", "B2" })
        {
            var ofLevel = doc.RootElement.EnumerateArray().Where(w => w.GetProperty("level").GetString() == level).ToList();
            var dailyAnswers = ofLevel.Count(w => !w.TryGetProperty("isDailyAnswer", out var d) || d.GetBoolean());
            Assert.True(dailyAnswers >= 7, $"Level {level} needs at least a week of daily words.");
            Assert.True(ofLevel.Count >= 30, $"Level {level} needs at least 30 words for unlimited training.");
        }
    }
}
