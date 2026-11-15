using System.Security.Claims;

namespace Wordrise.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public const string LevelClaim = "wordrise:level";

    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated user has no valid id claim.");
    }
}
