using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

public sealed class GameService(
    WordriseDbContext db,
    IAppClock clock,
    IValidGuessList validGuesses,
    SpacedRepetitionScheduler scheduler,
    ILogger<GameService> logger)
{
    private const string Blank = "_____";

    /// <summary>How many recent training words are avoided when picking the next one.</summary>
    private const int RecentTrainingWindow = 20;

    // ---------- Queries ----------

    /// <summary>Today's daily round for the user, or null if not started yet.</summary>
    public async Task<RoundState?> FindTodayDailyRoundAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        var roundId = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily && r.PlayedOn == today)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);

        return roundId is null ? null : await GetRoundAsync(userId, roundId.Value, ct);
    }

    /// <summary>Loads a round owned by the user (ownership enforced in the query: no IDOR).</summary>
    public async Task<RoundState> GetRoundAsync(int userId, int roundId, CancellationToken ct)
    {
        var round = await db.Rounds
            .AsNoTracking()
            .Include(r => r.Word)
            .Include(r => r.Attempts.OrderBy(a => a.Number))
            .Where(r => r.Id == roundId && r.UserId == userId)
            .FirstOrDefaultAsync(ct)
            ?? throw new GameException(GameErrorCode.NotFound, "Round not found.");

        return ToState(round);
    }

    // ---------- Commands ----------

    /// <summary>Returns today's daily round, creating it with the user's current level if needed (AC-01.1, AC-05.3).</summary>
    public async Task<RoundState> GetOrStartDailyRoundAsync(int userId, CancellationToken ct)
    {
        var existing = await FindTodayDailyRoundAsync(userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var level = await db.Users.Where(u => u.Id == userId).Select(u => (CefrLevel?)u.Level).FirstOrDefaultAsync(ct)
                    ?? throw new GameException(GameErrorCode.NotFound, "User not found.");

        var today = clock.Today;
        var dailyWord = await GetOrCreateDailyWordAsync(today, level, ct);

        var round = new Round
        {
            UserId = userId,
            Mode = RoundMode.Daily,
            DailyWordId = dailyWord.Id,
            WordId = dailyWord.WordId,
            PlayedOn = today,
            StartedAt = clock.UtcNow,
            HintsUnlocked = HintPolicy.AutoUnlocked(1)
        };
        db.Rounds.Add(round);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent request created the round first (double tap / two tabs).
            db.Entry(round).State = EntityState.Detached;
            return await FindTodayDailyRoundAsync(userId, ct)
                   ?? throw new GameException(GameErrorCode.Conflict, "Could not start today's round. Try again.");
        }

        return await GetRoundAsync(userId, round.Id, ct);
    }

    /// <summary>Starts (or resumes) a review round with the oldest due word (HU-04).</summary>
    public async Task<RoundState> GetOrStartReviewRoundAsync(int userId, CancellationToken ct)
    {
        var inProgressId = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Review && r.Status == RoundStatus.InProgress)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);

        if (inProgressId is not null)
        {
            return await GetRoundAsync(userId, inProgressId.Value, ct);
        }

        var today = clock.Today;
        var wordId = await db.PendingReviews
            .Where(p => p.UserId == userId && p.NextReviewOn <= today)
            .OrderBy(p => p.NextReviewOn).ThenBy(p => p.Id)
            .Select(p => (int?)p.WordId)
            .FirstOrDefaultAsync(ct)
            ?? throw new GameException(GameErrorCode.NothingToReview, "No words to review today. Nice climb!");

        var round = new Round
        {
            UserId = userId,
            Mode = RoundMode.Review,
            WordId = wordId,
            PlayedOn = today,
            StartedAt = clock.UtcNow,
            HintsUnlocked = HintPolicy.AutoUnlocked(1)
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync(ct);

        return await GetRoundAsync(userId, round.Id, ct);
    }

    /// <summary>In-progress training round (practice or listening), if any.</summary>
    public async Task<RoundState?> FindInProgressTrainingRoundAsync(int userId, CancellationToken ct)
    {
        var roundId = await db.Rounds
            .Where(r => r.UserId == userId && r.Status == RoundStatus.InProgress
                        && (r.Mode == RoundMode.Practice || r.Mode == RoundMode.Listening))
            .OrderByDescending(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);

        return roundId is null ? null : await GetRoundAsync(userId, roundId.Value, ct);
    }

    /// <summary>Training rounds finished today (shown on the Play screen).</summary>
    public Task<int> CountTrainingFinishedTodayAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        return db.Rounds.CountAsync(r => r.UserId == userId && r.PlayedOn == today
                                         && r.Status != RoundStatus.InProgress
                                         && (r.Mode == RoundMode.Practice || r.Mode == RoundMode.Listening), ct);
    }

    /// <summary>Distinct words from the user's finished daily rounds before today.</summary>
    public Task<int> CountPastDailyWordsAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        return db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily
                        && r.Status != RoundStatus.InProgress && r.PlayedOn < today)
            .Select(r => r.WordId)
            .Distinct()
            .CountAsync(ct);
    }

    /// <summary>
    /// Starts an unlimited training round. Resumes the in-progress round of the same mode instead of
    /// creating a second one. Today's daily words (all levels) are never used, so training cannot spoil them.
    /// </summary>
    public async Task<RoundState> StartTrainingRoundAsync(int userId, TrainingRequest request, CancellationToken ct)
    {
        if (request.Mode is not (RoundMode.Practice or RoundMode.Listening))
        {
            throw new GameException(GameErrorCode.NotAllowed, "Unknown training mode.");
        }

        var inProgressId = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == request.Mode && r.Status == RoundStatus.InProgress)
            .OrderByDescending(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (inProgressId is not null)
        {
            return await GetRoundAsync(userId, inProgressId.Value, ct);
        }

        var today = clock.Today;
        var todaysDailyWordIds = db.DailyWords.Where(d => d.Date == today).Select(d => d.WordId);

        var candidatesQuery = request.Source == TrainingSource.PastWords
            ? db.Rounds
                .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily
                            && r.Status != RoundStatus.InProgress && r.PlayedOn < today)
                .Select(r => r.WordId)
                .Distinct()
            : db.Words.Where(w => w.Level == request.Level).Select(w => w.Id);

        var candidates = await candidatesQuery
            .Where(id => !todaysDailyWordIds.Contains(id))
            .OrderBy(id => id)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            throw new GameException(GameErrorCode.NothingToPractice, request.Source == TrainingSource.PastWords
                ? "Play a few daily words first. They will show up here from tomorrow."
                : "There are no words to practice at this level yet.");
        }

        var recent = await db.Rounds
            .Where(r => r.UserId == userId && (r.Mode == RoundMode.Practice || r.Mode == RoundMode.Listening))
            .OrderByDescending(r => r.Id)
            .Take(Math.Min(RecentTrainingWindow, candidates.Count - 1))
            .Select(r => r.WordId)
            .ToListAsync(ct);

        var round = new Round
        {
            UserId = userId,
            Mode = request.Mode,
            WordId = TrainingWordPicker.Pick(candidates, recent.ToHashSet(), Random.Shared),
            PlayedOn = today,
            StartedAt = clock.UtcNow,
            HintsUnlocked = HintPolicy.AutoUnlocked(1)
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync(ct);

        return await GetRoundAsync(userId, round.Id, ct);
    }

    /// <summary>Ends a training round as missed and reveals the word. Not allowed for daily or review rounds.</summary>
    public async Task<RoundState> GiveUpAsync(int userId, int roundId, CancellationToken ct)
    {
        var round = await LoadTrackedRoundAsync(userId, roundId, ct);
        if (round.Mode is not (RoundMode.Practice or RoundMode.Listening))
        {
            throw new GameException(GameErrorCode.NotAllowed, "You can only skip words in training modes.");
        }

        await FinishRoundAsync(round, won: false, ct);
        await SaveRoundAsync(ct);
        return await GetRoundAsync(userId, roundId, ct);
    }

    /// <summary>Validates and scores one attempt (AC-01.2, AC-01.3, AC-04.1, AC-04.3).</summary>
    public async Task<RoundState> SubmitGuessAsync(int userId, int roundId, string? rawGuess, CancellationToken ct)
    {
        var round = await LoadTrackedRoundAsync(userId, roundId, ct);
        var answer = round.Word.Text;

        var guess = (rawGuess ?? string.Empty).Trim().ToLowerInvariant();
        if (guess.Length != answer.Length || !guess.All(char.IsAsciiLetterLower))
        {
            throw new GameException(GameErrorCode.InvalidGuess, $"Type a {answer.Length}-letter word using letters A–Z.");
        }

        if (guess != answer && !validGuesses.Contains(guess))
        {
            throw new GameException(GameErrorCode.UnknownWord, "That word is not in our list. Try another one.");
        }

        var states = GuessEvaluator.Evaluate(guess, answer);
        round.AttemptsUsed++;
        round.Attempts.Add(new Attempt
        {
            Number = round.AttemptsUsed,
            GuessText = guess,
            Feedback = GuessEvaluator.Encode(states),
            CreatedAt = clock.UtcNow
        });

        var won = guess == answer;
        if (won || round.AttemptsUsed >= GameRules.MaxAttempts)
        {
            await FinishRoundAsync(round, won, ct);
        }
        else
        {
            round.HintsUnlocked = Math.Max(round.HintsUnlocked, HintPolicy.AutoUnlocked(round.AttemptsUsed + 1));
        }

        await SaveRoundAsync(ct);
        return await GetRoundAsync(userId, roundId, ct);
    }

    /// <summary>"Use a hint (-10 pts)": unlocks the next hint before its attempt threshold.</summary>
    public async Task<RoundState> UseHintAsync(int userId, int roundId, CancellationToken ct)
    {
        var round = await LoadTrackedRoundAsync(userId, roundId, ct);
        if (round.HintsUnlocked >= GameRules.TotalHints)
        {
            throw new GameException(GameErrorCode.NoHintsLeft, "All hints are already unlocked.");
        }

        round.HintsUnlocked++;
        round.ManualHintsUsed++;
        await SaveRoundAsync(ct);
        return await GetRoundAsync(userId, roundId, ct);
    }

    /// <summary>Picture hint (emoji) for concrete words; costs points like an early hint.</summary>
    public async Task<RoundState> UseEmojiHintAsync(int userId, int roundId, CancellationToken ct)
    {
        var round = await LoadTrackedRoundAsync(userId, roundId, ct);
        if (string.IsNullOrEmpty(round.Word.Emoji))
        {
            throw new GameException(GameErrorCode.NotAllowed, "This word has no picture hint.");
        }

        if (round.EmojiHintUsed)
        {
            throw new GameException(GameErrorCode.NoHintsLeft, "The picture hint is already visible.");
        }

        round.EmojiHintUsed = true;
        await SaveRoundAsync(ct);
        return await GetRoundAsync(userId, roundId, ct);
    }

    // ---------- Internals ----------

    /// <summary>Daily round finished today: every 7-day streak earns a protector (max 2).</summary>
    private async Task AwardStreakFreezeAsync(int userId, DateOnly today, CancellationToken ct)
    {
        var playedBefore = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily
                        && r.Status != RoundStatus.InProgress && r.PlayedOn < today)
            .Select(r => r.PlayedOn)
            .ToListAsync(ct);
        var protectedDays = await db.StreakFreezeDays
            .Where(f => f.UserId == userId)
            .Select(f => f.Date)
            .ToListAsync(ct);

        var streak = StreakCalculator.Compute(playedBefore.Append(today), protectedDays, today);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.StreakFreezes = StreakFreezePolicy.AfterDailyFinished(user.StreakFreezes, streak.Current);
    }

    private async Task<Round> LoadTrackedRoundAsync(int userId, int roundId, CancellationToken ct)
    {
        var round = await db.Rounds
            .Include(r => r.Word)
            .Include(r => r.Attempts)
            .Where(r => r.Id == roundId && r.UserId == userId)
            .FirstOrDefaultAsync(ct)
            ?? throw new GameException(GameErrorCode.NotFound, "Round not found.");

        if (round.Status != RoundStatus.InProgress)
        {
            throw new GameException(GameErrorCode.RoundFinished, "This round is already finished.");
        }

        return round;
    }

    private async Task FinishRoundAsync(Round round, bool won, CancellationToken ct)
    {
        var today = clock.Today;
        round.Status = won ? RoundStatus.Won : RoundStatus.Lost;
        round.FinishedAt = clock.UtcNow;
        round.HintsUnlocked = GameRules.TotalHints;
        round.Score = ScoreCalculator.Calculate(won, round.AttemptsUsed, round.ManualHintsUsed + (round.EmojiHintUsed ? 1 : 0));
        round.Xp = XpCalculator.ForRound(round.Mode, won, round.Score);

        if (round.Mode == RoundMode.Daily)
        {
            await AwardStreakFreezeAsync(round.UserId, today, ct);
        }

        var review = await db.PendingReviews
            .FirstOrDefaultAsync(p => p.UserId == round.UserId && p.WordId == round.WordId, ct);

        if (!won)
        {
            var (stage, next) = scheduler.OnMissed(today);
            if (review is null)
            {
                db.PendingReviews.Add(new PendingReview
                {
                    UserId = round.UserId,
                    WordId = round.WordId,
                    Stage = stage,
                    NextReviewOn = next,
                    LastMissedOn = today
                });
            }
            else
            {
                review.Stage = stage;
                review.NextReviewOn = next;
                review.LastMissedOn = today;
            }
        }
        else if (review is not null && round.Mode == RoundMode.Review)
        {
            var (stage, next) = scheduler.OnRecalled(review.Stage, today);
            review.Stage = stage;
            review.NextReviewOn = next;
        }
    }

    private async Task SaveRoundAsync(CancellationToken ct)
    {
        try
        {
            // Round + attempt + pending review are saved atomically in a single SaveChanges transaction.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogInformation("Concurrent update on a round was rejected.");
            throw new GameException(GameErrorCode.Conflict, "This round changed in another tab. Reload and try again.");
        }
    }

    private async Task<DailyWord> GetOrCreateDailyWordAsync(DateOnly date, CefrLevel level, CancellationToken ct)
    {
        var existing = await db.DailyWords.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Date == date && d.Level == level, ct);
        if (existing is not null)
        {
            return existing;
        }

        var candidates = await db.Words
            .Where(w => w.Level == level && w.IsDailyAnswer)
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(ct);

        var created = new DailyWord { Date = date, Level = level, WordId = DailyWordSelector.Select(date, candidates) };
        db.DailyWords.Add(created);

        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(created).State = EntityState.Detached;
            return await db.DailyWords.AsNoTracking().FirstAsync(d => d.Date == date && d.Level == level, ct);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static RoundState ToState(Round round)
    {
        var word = round.Word;
        var finished = round.Status != RoundStatus.InProgress;

        var hintValues = new[]
        {
            ("Part of speech", word.PartOfSpeech.ToString().ToLowerInvariant()),
            ("Definition", word.DefinitionEn),
            ("Example sentence", word.ExampleSentence),
            ("First letter", word.Text[..1].ToUpperInvariant())
        };

        var hints = hintValues
            .Select((h, i) => new HintView(
                Number: i + 1,
                Label: h.Item1,
                Value: i < round.HintsUnlocked ? h.Item2 : null,
                UnlocksAtAttempt: HintPolicy.UnlockAttemptFor(i + 1)))
            .ToList();

        var attempts = round.Attempts
            .OrderBy(a => a.Number)
            .Select(a => new AttemptView(a.GuessText, GuessEvaluator.Decode(a.Feedback)))
            .ToList();

        RoundResultView? result = finished
            ? new RoundResultView(
                Word: word.Text,
                PartOfSpeech: word.PartOfSpeech.ToString().ToLowerInvariant(),
                Definition: word.DefinitionEn,
                Example: word.ExampleSentence.Replace(Blank, word.Text, StringComparison.Ordinal),
                Translation: word.TranslationEs,
                Level: word.Level,
                Score: round.Score,
                Won: round.Status == RoundStatus.Won)
            : null;

        return new RoundState(
            round.Id, round.Mode, round.PlayedOn, word.Level, word.Text.Length, round.AttemptsUsed, GameRules.MaxAttempts,
            round.Status, round.HintsUnlocked, GameRules.TotalHints, round.ManualHintsUsed, hints, attempts, result,
            SpokenWord: round.Mode == RoundMode.Listening ? word.Text : null,
            EmojiAvailable: !string.IsNullOrEmpty(word.Emoji),
            EmojiHintUsed: round.EmojiHintUsed,
            Emoji: round.EmojiHintUsed || finished ? word.Emoji : null,
            Xp: round.Xp);
    }
}
