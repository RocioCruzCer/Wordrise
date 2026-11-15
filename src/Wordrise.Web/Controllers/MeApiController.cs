using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;

namespace Wordrise.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
[EnableRateLimiting("game")]
public sealed class MeApiController(EngagementService engagement) : ControllerBase
{
    /// <summary>
    /// Goal, weekly XP, combo, streak and achievements unlocked since the last call.
    /// POST because it marks new achievements as seen.
    /// </summary>
    [HttpPost("progress")]
    public async Task<ActionResult<ProgressSnapshot>> Progress(CancellationToken ct) =>
        await engagement.GetProgressAsync(User.GetUserId(), ct);
}
