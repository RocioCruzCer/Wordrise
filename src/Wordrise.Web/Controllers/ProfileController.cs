using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Authorize]
[Route("profile")]
public sealed class ProfileController(AccountService accounts, StatsService stats, EngagementService engagement) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool saved = false, CancellationToken ct = default)
    {
        var model = await BuildAsync(null, saved, ct);
        return model is null ? (IActionResult)this.SignOutToLogin() : View(model);
    }

    [HttpPost("")]
    public async Task<IActionResult> Index([Bind(Prefix = "Settings")] SettingsInput input, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!ModelState.IsValid)
        {
            var invalid = await BuildAsync(input, false, ct);
            return invalid is null ? (IActionResult)this.SignOutToLogin() : View(invalid);
        }

        await accounts.UpdateSettingsAsync(userId, new UserSettings(
            input.Level!.Value, input.PronunciationAudio, input.DailyGoal, input.SoundEnabled, input.ShowInLeagues), ct);

        // Refresh the cookie so the sidebar shows the new level.
        var user = await accounts.GetAsync(userId, ct);
        if (user is not null)
        {
            await AccountController.SignInAsync(HttpContext, user);
        }

        return RedirectToAction(nameof(Index), new { saved = true });
    }

    private async Task<ProfileModel?> BuildAsync(SettingsInput? input, bool saved, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        var userStats = await stats.GetStatsAsync(userId, ct);
        return new ProfileModel(
            user,
            userStats,
            stats.GetCamp(userStats.WordsLearned),
            input ?? new SettingsInput
            {
                Level = user.Level,
                PronunciationAudio = user.PronunciationAudioEnabled,
                DailyGoal = user.DailyGoal,
                SoundEnabled = user.SoundEnabled,
                ShowInLeagues = user.ShowInLeagues
            },
            saved,
            await engagement.EvaluateAchievementsAsync(userId, ct));
    }
}
