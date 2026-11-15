using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;
using Wordrise.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Render injects PORT for Docker services.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ---------- Options & domain services ----------
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAppClock, AppClock>();
builder.Services.AddSingleton(sp =>
    new SpacedRepetitionScheduler(sp.GetRequiredService<IOptions<GameOptions>>().Value.ReviewIntervalsInDays));
builder.Services.AddSingleton(sp =>
    new CampCalculator(sp.GetRequiredService<IOptions<GameOptions>>().Value.Camps
        .Select(c => new Camp(c.Name, c.MinWordsLearned))));
builder.Services.AddSingleton<IValidGuessList>(sp =>
    ValidGuessList.LoadFromFile(Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
        "Data", "Seed", "valid-guesses.txt")));

// ---------- Data ----------
builder.Services.AddDbContext<WordriseDbContext>(options =>
    options.UseNpgsql(ConnectionStringResolver.Resolve(builder.Configuration),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDataProtection()
    .SetApplicationName("Wordrise")
    .PersistKeysToDbContext<WordriseDbContext>();

builder.Services.AddScoped<DbSeeder>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<EngagementService>();
builder.Services.AddScoped<LeagueService>();
builder.Services.AddScoped<SprintService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// ---------- Auth ----------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.Cookie.Name = "wordrise.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            // JSON endpoints get 401 so the client can show "session expired" (proposal §3.5).
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "wordrise.csrf";
});

builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- Abuse protection ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("game", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Render terminates TLS at its proxy, whose addresses are not published. Trust forwarded headers from it.
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<WordriseDbContext>();

var app = builder.Build();

// ---------- Database migration & seed ----------
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WordriseDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedWordsAsync();
}

// Fail fast if the guess list is missing.
_ = app.Services.GetRequiredService<IValidGuessList>();

// ---------- Pipeline ----------
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/home/status/{0}");

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "DENY";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    if (!app.Environment.IsDevelopment())
    {
        // Not applied in Development: Visual Studio hot reload injects scripts and a websocket on another port.
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "connect-src 'self'; manifest-src 'self'; worker-src 'self'; frame-ancestors 'none'; " +
            "base-uri 'self'; form-action 'self'; object-src 'none'";
    }

    await next();
});

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        // The service worker and manifest must be revalidated so updates are detected promptly.
        var path = ctx.Context.Request.Path.Value;
        if (path is "/sw.js" or "/manifest.webmanifest" or "/offline.html")
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
        else if (ctx.Context.Request.Query.ContainsKey("v"))
        {
            // Fingerprinted by asp-append-version: safe to cache for a long time.
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;
