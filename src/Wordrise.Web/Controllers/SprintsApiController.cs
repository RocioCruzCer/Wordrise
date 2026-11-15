using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;

namespace Wordrise.Web.Controllers;

public sealed record SprintAnswerRequest(int Challenge, string? Guess);

public sealed record SprintSkipRequest(int Challenge);

/// <summary>JSON endpoints for the 60-second sprint. Ownership is enforced in every query.</summary>
[ApiController]
[Authorize]
[Route("api/sprints")]
[EnableRateLimiting("game")]
public sealed class SprintsApiController(SprintService sprints) : ControllerBase
{
    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) =>
        Run(() => sprints.GetAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:int}/answer")]
    public Task<IActionResult> Answer(int id, [FromBody] SprintAnswerRequest request, CancellationToken ct) =>
        Run(() => sprints.AnswerAsync(User.GetUserId(), id, request.Challenge, request.Guess, ct));

    [HttpPost("{id:int}/skip")]
    public Task<IActionResult> Skip(int id, [FromBody] SprintSkipRequest request, CancellationToken ct) =>
        Run(() => sprints.SkipAsync(User.GetUserId(), id, request.Challenge, ct));

    [HttpPost("{id:int}/finish")]
    public Task<IActionResult> Finish(int id, CancellationToken ct) =>
        Run(() => sprints.FinishAsync(User.GetUserId(), id, ct));

    private async Task<IActionResult> Run(Func<Task<SprintState>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (GameException ex)
        {
            var status = ex.Code == GameErrorCode.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return Problem(detail: ex.Message, statusCode: status, title: ex.Code.ToString());
        }
    }
}
