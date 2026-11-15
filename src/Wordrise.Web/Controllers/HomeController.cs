using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Authorize]
public sealed class HomeController(
    AccountService accounts,
    GameService game,
    StatsService stats,
    EngagementService engagement,
    IAppClock clock) : Controller
{
    private const int DashboardReviewItems = 3;

    [HttpGet("/")]
    [HttpGet("/home")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        await engagement.ProtectStreakAsync(userId, ct);
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            // Cookie points to a deleted user.
            return this.SignOutToLogin();
        }

        var userStats = await stats.GetStatsAsync(userId, ct);
        var model = new DashboardModel(
            user,
            await game.FindTodayDailyRoundAsync(userId, ct),
            userStats,
            stats.GetCamp(userStats.WordsLearned),
            await stats.GetDueReviewsAsync(userId, DashboardReviewItems, ct),
            Greeting(clock.LocalNow),
            clock.Today,
            await engagement.GetGoalAsync(userId, ct),
            await engagement.GetWeeklyXpAsync(userId, ct));

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet("/home/error")]
    public IActionResult Error() => View("Error", 500);

    [AllowAnonymous]
    [HttpGet("/home/status/{code:int}")]
    public IActionResult Status(int code)
    {
        var feature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (feature?.OriginalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
        {
            return StatusCode(code);
        }

        return View("Error", code);
    }

    private static string Greeting(DateTimeOffset localNow) =>
        localNow.Hour switch
        {
            < 12 => "Good morning",
            < 19 => "Good afternoon",
            _ => "Good evening"
        };
}
