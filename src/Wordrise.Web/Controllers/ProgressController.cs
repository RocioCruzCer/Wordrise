using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Authorize]
[Route("progress")]
public sealed class ProgressController(AccountService accounts, StatsService stats) : Controller
{
    private const int NotebookPreview = 3;
    private const int NotebookPageSize = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return this.SignOutToLogin();
        }

        var userStats = await stats.GetStatsAsync(userId, ct);
        return View(new ProgressModel(
            user,
            userStats,
            stats.GetCamp(userStats.WordsLearned),
            await stats.GetLastSevenDaysAsync(userId, ct),
            await stats.GetNotebookAsync(userId, NotebookPreview, 0, ct)));
    }

    /// <summary>"Notebook of peaks — See all", paginated.</summary>
    [HttpGet("notebook")]
    public async Task<IActionResult> Notebook(int page = 1, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        page = Math.Max(1, page);
        var items = await stats.GetNotebookAsync(userId, NotebookPageSize + 1, (page - 1) * NotebookPageSize, ct);
        return View(new NotebookPageModel(items.Take(NotebookPageSize).ToList(), page, items.Count > NotebookPageSize));
    }
}
