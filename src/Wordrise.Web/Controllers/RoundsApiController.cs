using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

/// <summary>
/// JSON endpoints used by the round screen via fetch. The answer is only included once the round ends.
/// CSRF: protected by the global antiforgery filter (token sent in the X-CSRF-TOKEN header).
/// </summary>
[ApiController]
[Authorize]
[Route("api/rounds")]
[EnableRateLimiting("game")]
public sealed class RoundsApiController(GameService game) : ControllerBase
{
    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) =>
        Run(() => game.GetRoundAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:int}/guesses")]
    public Task<IActionResult> Guess(int id, [FromBody] GuessRequest request, CancellationToken ct) =>
        Run(() => game.SubmitGuessAsync(User.GetUserId(), id, request.Guess, ct));

    [HttpPost("{id:int}/hints")]
    public Task<IActionResult> Hint(int id, CancellationToken ct) =>
        Run(() => game.UseHintAsync(User.GetUserId(), id, ct));

    /// <summary>Picture hint (emoji) for concrete words, -10 points.</summary>
    [HttpPost("{id:int}/emoji-hint")]
    public Task<IActionResult> EmojiHint(int id, CancellationToken ct) =>
        Run(() => game.UseEmojiHintAsync(User.GetUserId(), id, ct));

    /// <summary>Skip the word in a training round (counts as missed and goes to review).</summary>
    [HttpPost("{id:int}/give-up")]
    public Task<IActionResult> GiveUp(int id, CancellationToken ct) =>
        Run(() => game.GiveUpAsync(User.GetUserId(), id, ct));

    private async Task<IActionResult> Run(Func<Task<RoundState>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (GameException ex)
        {
            var status = ex.Code switch
            {
                GameErrorCode.NotFound => StatusCodes.Status404NotFound,
                GameErrorCode.RoundFinished or GameErrorCode.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Problem(detail: ex.Message, statusCode: status, title: ex.Code.ToString());
        }
    }
}
