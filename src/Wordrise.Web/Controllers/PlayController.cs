using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wordrise.Web.Domain;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Authorize]
[Route("play")]
public sealed class PlayController(
    AccountService accounts,
    GameService game,
    StatsService stats,
    EngagementService engagement,
    SprintService sprints,
    IAppClock clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        await engagement.ProtectStreakAsync(userId, ct);
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return this.SignOutToLogin();
        }

        var userStats = await stats.GetStatsAsync(userId, ct);
        return View(new PlayHubModel(
            user,
            await game.FindTodayDailyRoundAsync(userId, ct),
            userStats.ToReview,
            await game.FindInProgressTrainingRoundAsync(userId, ct),
            await game.CountTrainingFinishedTodayAsync(userId, ct),
            await game.CountPastDailyWordsAsync(userId, ct),
            await engagement.GetGoalAsync(userId, ct),
            await sprints.GetBestScoreAsync(userId, ct)));
    }

    /// <summary>Starts or resumes today's word (POST: creates state).</summary>
    [HttpPost("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var round = await game.GetOrStartDailyRoundAsync(User.GetUserId(), ct);
        return RedirectToAction(nameof(Round), new { id = round.RoundId });
    }

    [HttpPost("review")]
    public async Task<IActionResult> Review(CancellationToken ct)
    {
        try
        {
            var round = await game.GetOrStartReviewRoundAsync(User.GetUserId(), ct);
            return RedirectToAction(nameof(Round), new { id = round.RoundId });
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NothingToReview)
        {
            TempData["Flash"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>Unlimited practice. <paramref name="level"/> is A2/B1/B2 or "past" (own past daily words).</summary>
    [HttpPost("practice")]
    public Task<IActionResult> Practice(string? level, CancellationToken ct) => StartTraining(RoundMode.Practice, level, ct);

    /// <summary>Unlimited listening challenge: the word is spoken first, then guessed.</summary>
    [HttpPost("listening")]
    public Task<IActionResult> Listening(string? level, CancellationToken ct) => StartTraining(RoundMode.Listening, level, ct);

    [HttpGet("round/{id:int}")]
    public async Task<IActionResult> Round(int id, string? next, CancellationToken ct)
    {
        var userId = User.GetUserId();
        try
        {
            var round = await game.GetRoundAsync(userId, id, ct);
            var user = await accounts.GetAsync(userId, ct);

            // "next" only drives the "Next word" button; it is validated, never trusted as-is.
            TrainingRequest? nextRequest =
                TrainingRequest.TryParse(round.Mode, next, user?.Level ?? round.Level, out var parsed) ? parsed : null;

            return View(new RoundPageModel(
                round,
                user?.PronunciationAudioEnabled ?? true,
                (int)Math.Ceiling(clock.UntilNextDay.TotalSeconds),
                nextRequest,
                user?.SoundEnabled ?? true));
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NotFound)
        {
            return NotFound();
        }
    }

    /// <summary>60-second anagram sprint at the chosen level (defaults to the user's level).</summary>
    [HttpPost("sprint")]
    public async Task<IActionResult> Sprint(string? level, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return this.SignOutToLogin();
        }

        var chosen = user.Level;
        if (!string.IsNullOrWhiteSpace(level)
            && !(Enum.TryParse(level, ignoreCase: true, out chosen) && Enum.IsDefined(chosen)))
        {
            TempData["Flash"] = "Choose a valid level.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var sprint = await sprints.StartAsync(userId, chosen, ct);
            return RedirectToAction(nameof(SprintPage), new { id = sprint.Id });
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NothingToPractice)
        {
            TempData["Flash"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("sprint/{id:int}")]
    public async Task<IActionResult> SprintPage(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        try
        {
            var sprint = await sprints.GetAsync(userId, id, ct);
            var user = await accounts.GetAsync(userId, ct);
            return View("Sprint", new SprintPageModel(sprint, user?.SoundEnabled ?? true));
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NotFound)
        {
            return NotFound();
        }
    }

    private async Task<IActionResult> StartTraining(RoundMode mode, string? token, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return this.SignOutToLogin();
        }

        if (!TrainingRequest.TryParse(mode, token, user.Level, out var request))
        {
            TempData["Flash"] = "Choose a valid level.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var round = await game.StartTrainingRoundAsync(userId, request, ct);
            return RedirectToAction(nameof(Round), new { id = round.RoundId, next = request.Token });
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NothingToPractice)
        {
            TempData["Flash"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
