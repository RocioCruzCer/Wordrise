using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;
using Wordrise.Web.ViewModels;

namespace Wordrise.Web.Controllers;

[Route("account")]
public sealed class AccountController(AccountService accounts) : Controller
{
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? tab, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new AccountPageModel
        {
            ActiveTab = tab == "signup" ? "signup" : "login",
            Login = new LoginInput { ReturnUrl = returnUrl }
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([Bind(Prefix = "Login")] LoginInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(new AccountPageModel { ActiveTab = "login", Login = input });
        }

        var user = await accounts.ValidateCredentialsAsync(input.Email, input.Password, ct);
        if (user is null)
        {
            ModelState.AddModelError("Login.Password", "Email or password is incorrect.");
            input.Password = string.Empty;
            return View(new AccountPageModel { ActiveTab = "login", Login = input });
        }

        await SignInAsync(HttpContext, user);
        return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SignUp([Bind(Prefix = "SignUp")] SignUpInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            input.Password = string.Empty;
            return View("Login", new AccountPageModel { ActiveTab = "signup", SignUp = input });
        }

        var (result, user) = await accounts.RegisterAsync(input.Name, input.Email, input.Password, input.Level!.Value, ct);
        if (result == RegisterResult.EmailTaken || user is null)
        {
            ModelState.AddModelError("SignUp.Email", "An account with this email already exists.");
            input.Password = string.Empty;
            return View("Login", new AccountPageModel { ActiveTab = "signup", SignUp = input });
        }

        await SignInAsync(HttpContext, user);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Issues (or re-issues after a profile change) the auth cookie.</summary>
    internal static Task SignInAsync(HttpContext http, UserSummary user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Name),
            new(ClaimsPrincipalExtensions.LevelClaim, user.Level.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
