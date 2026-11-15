using Microsoft.EntityFrameworkCore;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

public sealed record SprintChallenge(int Number, string Letters, int Length, string Definition, string PartOfSpeech);

public sealed record SprintWordResult(string Word, string Definition, string Translation, bool Solved);

public sealed record SprintState(
    int Id,
    CefrLevel Level,
    bool Finished,
    double SecondsLeft,
    int Score,
    int Skipped,
    SprintChallenge? Challenge,
    IReadOnlyList<SprintWordResult>? Results,
    int Xp,
    int BestScore,
    bool? LastAnswerCorrect);

/// <summary>
/// 60-second anagram sprint: the client gets scrambled letters and the definition; the server keeps the word,
/// the clock and the score. Answers carry the challenge number so double submits are ignored.
/// </summary>
public sealed class SprintService(WordriseDbContext db, IAppClock clock, IValidGuessList validGuesses)
{
    public async Task<SprintState> StartAsync(int userId, CefrLevel level, CancellationToken ct)
    {
        var open = await db.SprintSessions
            .Include(s => s.CurrentWord)
            .Where(s => s.UserId == userId && s.FinishedAt == null)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            if (!IsExpired(open))
            {
                return await ToStateAsync(open, null, ct);
            }

            Finalize(open);
        }

        var now = clock.UtcNow;
        var session = new SprintSession
        {
            UserId = userId,
            Level = level,
            PlayedOn = clock.Today,
            StartedAt = now,
            EndsAt = now + SprintRules.Duration
        };
        db.SprintSessions.Add(session);

        if (!await NextChallengeAsync(session, ct))
        {
            throw new GameException(GameErrorCode.NothingToPractice, "There are no words for a sprint at this level yet.");
        }

        await db.SaveChangesAsync(ct);
        return await ToStateAsync(session, null, ct);
    }

    public async Task<SprintState> GetAsync(int userId, int sessionId, CancellationToken ct)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (session.FinishedAt is null && IsExpired(session))
        {
            Finalize(session);
            await db.SaveChangesAsync(ct);
        }

        return await ToStateAsync(session, null, ct);
    }

    public async Task<SprintState> AnswerAsync(int userId, int sessionId, int challenge, string? rawGuess, CancellationToken ct)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (await CloseIfOverAsync(session, ct) || challenge != session.ChallengeNumber || session.CurrentWord is null)
        {
            return await ToStateAsync(session, null, ct); // finished or stale: nothing to score
        }

        var guess = (rawGuess ?? string.Empty).Trim().ToLowerInvariant();
        var answer = session.CurrentWord.Text;

        // The definition points to one word, but any valid word with exactly the same letters is accepted.
        var correct = guess == answer || (SprintRules.IsAnagram(guess, answer) && validGuesses.Contains(guess));
        if (!correct)
        {
            return await ToStateAsync(session, false, ct);
        }

        session.Score++;
        session.SolvedWordIds = [.. session.SolvedWordIds, session.CurrentWordId!.Value];
        if (!await NextChallengeAsync(session, ct))
        {
            Finalize(session);
        }

        await db.SaveChangesAsync(ct);
        return await ToStateAsync(session, true, ct);
    }

    public async Task<SprintState> SkipAsync(int userId, int sessionId, int challenge, CancellationToken ct)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (await CloseIfOverAsync(session, ct) || challenge != session.ChallengeNumber)
        {
            return await ToStateAsync(session, null, ct);
        }

        session.Skipped++;
        if (!await NextChallengeAsync(session, ct))
        {
            Finalize(session);
        }

        await db.SaveChangesAsync(ct);
        return await ToStateAsync(session, null, ct);
    }

    /// <summary>Called by the client when its timer reaches zero. Ignored if the server clock says time is left.</summary>
    public async Task<SprintState> FinishAsync(int userId, int sessionId, CancellationToken ct)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (session.FinishedAt is null && clock.UtcNow >= session.EndsAt - TimeSpan.FromSeconds(1))
        {
            Finalize(session);
            await db.SaveChangesAsync(ct);
        }

        return await ToStateAsync(session, null, ct);
    }

    public async Task<int> GetBestScoreAsync(int userId, CancellationToken ct) =>
        await db.SprintSessions.Where(s => s.UserId == userId && s.FinishedAt != null)
            .MaxAsync(s => (int?)s.Score, ct) ?? 0;

    // ---------- Internals ----------

    private async Task<SprintSession> LoadAsync(int userId, int sessionId, CancellationToken ct) =>
        await db.SprintSessions
            .Include(s => s.CurrentWord)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
        ?? throw new GameException(GameErrorCode.NotFound, "Sprint not found.");

    private bool IsExpired(SprintSession s) => clock.UtcNow > s.EndsAt + SprintRules.Grace;

    private async Task<bool> CloseIfOverAsync(SprintSession session, CancellationToken ct)
    {
        if (session.FinishedAt is not null)
        {
            return true;
        }

        if (!IsExpired(session))
        {
            return false;
        }

        Finalize(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private void Finalize(SprintSession session)
    {
        session.FinishedAt = clock.UtcNow;
        session.Xp = XpCalculator.ForSprint(session.Score);
    }

    /// <summary>Picks an unseen word of the level (never today's daily words) and scrambles it.</summary>
    private async Task<bool> NextChallengeAsync(SprintSession session, CancellationToken ct)
    {
        var today = clock.Today;
        var todaysDaily = db.DailyWords.Where(d => d.Date == today).Select(d => d.WordId);
        var seen = session.SeenWordIds;

        var candidates = await db.Words
            .Where(w => w.Level == session.Level && !todaysDaily.Contains(w.Id) && !seen.Contains(w.Id))
            .Select(w => new { w.Id, w.Text })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return false;
        }

        var pick = candidates[Random.Shared.Next(candidates.Count)];
        session.CurrentWordId = pick.Id;
        session.CurrentLetters = SprintRules.Scramble(pick.Text, Random.Shared).ToUpperInvariant();
        session.ChallengeNumber++;
        session.SeenWordIds = [.. session.SeenWordIds, pick.Id];
        return true;
    }

    private async Task<SprintState> ToStateAsync(SprintSession session, bool? lastAnswerCorrect, CancellationToken ct)
    {
        var finished = session.FinishedAt is not null;
        SprintChallenge? challenge = null;
        IReadOnlyList<SprintWordResult>? results = null;

        if (!finished && session.CurrentWordId is not null)
        {
            var word = await db.Words.AsNoTracking()
                .Where(w => w.Id == session.CurrentWordId)
                .Select(w => new { w.Text, w.DefinitionEn, w.PartOfSpeech })
                .FirstAsync(ct);
            challenge = new SprintChallenge(
                session.ChallengeNumber, session.CurrentLetters, word.Text.Length, word.DefinitionEn,
                word.PartOfSpeech.ToString().ToLowerInvariant());
        }

        if (finished)
        {
            var ids = session.SeenWordIds;
            var words = await db.Words.AsNoTracking()
                .Where(w => ids.Contains(w.Id))
                .Select(w => new { w.Id, w.Text, w.DefinitionEn, w.TranslationEs })
                .ToDictionaryAsync(w => w.Id, ct);
            var solved = session.SolvedWordIds.ToHashSet();
            results = ids
                .Where(words.ContainsKey)
                .Select(id => new SprintWordResult(words[id].Text, words[id].DefinitionEn, words[id].TranslationEs, solved.Contains(id)))
                .ToList();
        }

        var secondsLeft = finished ? 0 : Math.Max(0, (session.EndsAt - clock.UtcNow).TotalSeconds);
        return new SprintState(
            session.Id, session.Level, finished, Math.Round(secondsLeft, 1), session.Score, session.Skipped,
            challenge, results, session.Xp, await GetBestScoreAsync(session.UserId, ct), lastAnswerCorrect);
    }
}
