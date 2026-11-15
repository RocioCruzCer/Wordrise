using Microsoft.EntityFrameworkCore;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

/// <summary>
/// Statistics computed from stored rounds, without extra tables (proposal §3.4).
/// ASSUMPTIONS (pending confirmation):
/// - Words learned = distinct words with at least one won round.
/// - Accuracy = won rounds / finished rounds.
/// - Average attempts = mean attempts over finished rounds (a lost round counts 6).
/// - To review = pending reviews whose date is today or earlier.
/// - Listening rounds are excluded from learned words, accuracy and averages: the client receives the word
///   to speak it, so those results are not a reliable measure. Daily, review and practice rounds count.
/// </summary>
public sealed class StatsService(WordriseDbContext db, IAppClock clock, CampCalculator camps)
{
    public async Task<UserStats> GetStatsAsync(int userId, CancellationToken ct)
    {
        var finished = db.Rounds.Where(r => r.UserId == userId && r.Status != RoundStatus.InProgress
                                            && r.Mode != RoundMode.Listening);

        var aggregate = await finished
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Won = g.Count(r => r.Status == RoundStatus.Won),
                AvgAttempts = g.Average(r => (double)r.AttemptsUsed)
            })
            .FirstOrDefaultAsync(ct);

        var wordsLearned = await finished
            .Where(r => r.Status == RoundStatus.Won)
            .Select(r => r.WordId)
            .Distinct()
            .CountAsync(ct);

        var today = clock.Today;
        var toReview = await db.PendingReviews.CountAsync(p => p.UserId == userId && p.NextReviewOn <= today, ct);

        if (aggregate is null || aggregate.Total == 0)
        {
            return new UserStats(0, 0, 0, toReview, 0, 0, 0);
        }

        // One row per finished daily round (at most one per day), so this stays small.
        var playedDays = await finished
            .Where(r => r.Mode == RoundMode.Daily)
            .Select(r => r.PlayedOn)
            .ToListAsync(ct);
        var protectedDays = await db.StreakFreezeDays
            .Where(f => f.UserId == userId)
            .Select(f => f.Date)
            .ToListAsync(ct);
        var streak = StreakCalculator.Compute(playedDays, protectedDays, today);

        return new UserStats(
            WordsLearned: wordsLearned,
            AccuracyPercent: (int)Math.Round(aggregate.Won * 100.0 / aggregate.Total),
            AverageAttempts: Math.Round(aggregate.AvgAttempts, 1),
            ToReview: toReview,
            RoundsFinished: aggregate.Total,
            CurrentStreak: streak.Current,
            BestStreak: streak.Best);
    }

    public CampProgress GetCamp(int wordsLearned) => camps.For(wordsLearned);

    /// <summary>Review words due today or earlier, oldest first (AC-04.2).</summary>
    public async Task<IReadOnlyList<ReviewItem>> GetDueReviewsAsync(int userId, int take, CancellationToken ct)
    {
        var today = clock.Today;
        return await db.PendingReviews
            .Where(p => p.UserId == userId && p.NextReviewOn <= today)
            .OrderBy(p => p.NextReviewOn).ThenBy(p => p.Id)
            .Take(take)
            .Select(p => new ReviewItem(p.WordId, p.Word.Text, p.Word.Level, p.LastMissedOn))
            .ToListAsync(ct);
    }

    /// <summary>Attempts per day for daily rounds over the last 7 days (AC-03.2).</summary>
    public async Task<IReadOnlyList<DayAttempts>> GetLastSevenDaysAsync(int userId, CancellationToken ct)
    {
        var today = clock.Today;
        var from = today.AddDays(-6);

        var rows = await db.Rounds
            .Where(r => r.UserId == userId && r.Mode == RoundMode.Daily
                        && r.Status != RoundStatus.InProgress
                        && r.PlayedOn >= from && r.PlayedOn <= today)
            .Select(r => new { r.PlayedOn, r.AttemptsUsed })
            .ToListAsync(ct);

        var byDay = rows.ToDictionary(r => r.PlayedOn, r => r.AttemptsUsed);
        return Enumerable.Range(0, 7)
            .Select(i => from.AddDays(i))
            .Select(d => new DayAttempts(d, byDay.TryGetValue(d, out var a) ? a : null, d == today))
            .ToList();
    }

    /// <summary>"Notebook of peaks": learned words, most recent first.</summary>
    public async Task<IReadOnlyList<NotebookItem>> GetNotebookAsync(int userId, int take, int skip, CancellationToken ct)
    {
        var learned = db.Rounds
            .Where(r => r.UserId == userId && r.Status == RoundStatus.Won && r.Mode != RoundMode.Listening)
            .GroupBy(r => r.WordId)
            .Select(g => new { WordId = g.Key, LastWon = g.Max(r => r.FinishedAt) });

        var items = await learned
            .Join(db.Words, x => x.WordId, w => w.Id,
                (x, w) => new { x.LastWon, w.Id, w.Text, w.Level, w.DefinitionEn, w.ExampleSentence, w.TranslationEs })
            .OrderByDescending(x => x.LastWon).ThenBy(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return items
            .Select(w => new NotebookItem(w.Text, w.Level, w.DefinitionEn,
                w.ExampleSentence.Replace("_____", w.Text, StringComparison.Ordinal), w.TranslationEs))
            .ToList();
    }
}
