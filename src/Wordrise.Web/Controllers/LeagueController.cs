using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wordrise.Web.Domain;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Authorize]
[Route("league")]
[EnableRateLimiting("game")]
public sealed class LeagueController(
    AccountService accounts,
    LeagueService leagues,
    EngagementService engagement) : Controller
{
    /// <summary>Weekly level league (A2/B1/B2) plus the user's groups.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? level, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await accounts.GetAsync(userId, ct);
        if (user is null)
        {
            return this.SignOutToLogin();
        }

        var chosen = user.Level;
        if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<CefrLevel>(level, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            chosen = parsed;
        }

        return View(new LeagueModel(
            user,
            chosen,
            await leagues.GetLevelBoardAsync(userId, chosen, ct),
            await leagues.GetMyGroupsAsync(userId, ct),
            await engagement.GetWeeklyXpAsync(userId, ct)));
    }

    [HttpGet("groups/{id:int}")]
    public async Task<IActionResult> Group(int id, CancellationToken ct)
    {
        try
        {
            var (group, board) = await leagues.GetGroupBoardAsync(User.GetUserId(), id, ct);
            return View(new GroupPageModel(group, board));
        }
        catch (GameException ex) when (ex.Code == GameErrorCode.NotFound)
        {
            return NotFound();
        }
    }

    [HttpPost("groups")]
    public Task<IActionResult> Create(string? name, CancellationToken ct) =>
        GroupAction(async userId =>
        {
            var group = await leagues.CreateGroupAsync(userId, name, ct);
            TempData["Flash"] = $"Group created. Share the code {group.Code} with your classmates.";
            return group.Id;
        }, ct);

    [HttpPost("groups/join")]
    public Task<IActionResult> Join(string? code, CancellationToken ct) =>
        GroupAction(async userId =>
        {
            var group = await leagues.JoinGroupAsync(userId, code, ct);
            TempData["Flash"] = $"You joined {group.Name}.";
            return group.Id;
        }, ct);

    [HttpPost("groups/{id:int}/leave")]
    public async Task<IActionResult> Leave(int id, CancellationToken ct)
    {
        try
        {
            await leagues.LeaveGroupAsync(User.GetUserId(), id, ct);
            TempData["Flash"] = "You left the group.";
        }
        catch (GameException ex)
        {
            TempData["Flash"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> GroupAction(Func<int, Task<int>> action, CancellationToken ct)
    {
        var userId = User.GetUserId();
        try
        {
            var groupId = await action(userId);
            await engagement.EvaluateAchievementsAsync(userId, ct); // "Team player"
            return RedirectToAction(nameof(Group), new { id = groupId });
        }
        catch (GameException ex)
        {
            TempData["Flash"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
