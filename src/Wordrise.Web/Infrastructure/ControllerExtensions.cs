using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Wordrise.Web.Infrastructure;

public static class ControllerExtensions
{
    /// <summary>Used when the cookie points to a user that no longer exists.</summary>
    public static SignOutResult SignOutToLogin(this Controller controller) =>
        controller.SignOut(
            new AuthenticationProperties { RedirectUri = "/account/login" },
            CookieAuthenticationDefaults.AuthenticationScheme);
}
