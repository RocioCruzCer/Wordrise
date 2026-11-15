using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

public sealed record UserSummary(
    int Id,
    string Name,
    string Email,
    CefrLevel Level,
    bool PronunciationAudioEnabled,
    int DailyGoal,
    int StreakFreezes,
    bool SoundEnabled,
    bool ShowInLeagues);

public sealed record UserSettings(CefrLevel Level, bool PronunciationAudio, int DailyGoal, bool SoundEnabled, bool ShowInLeagues);

public enum RegisterResult { Created, EmailTaken }

/// <summary>Account management. Passwords are hashed with ASP.NET Core's PasswordHasher (PBKDF2, salted).</summary>
public sealed class AccountService(WordriseDbContext db, IPasswordHasher<User> hasher, IAppClock clock)
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<(RegisterResult Result, UserSummary? User)> RegisterAsync(
        string name, string email, string password, CefrLevel level, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
        {
            return (RegisterResult.EmailTaken, null);
        }

        var user = new User
        {
            Name = name.Trim(),
            Email = normalized,
            Level = level,
            CreatedAt = clock.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return (RegisterResult.EmailTaken, null);
        }

        return (RegisterResult.Created, ToSummary(user));
    }

    /// <summary>Returns the user when credentials are valid; null otherwise (same response for unknown email and bad password).</summary>
    public async Task<UserSummary?> ValidateCredentialsAsync(string email, string password, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            // Equalize timing with the "bad password" path to reduce user enumeration.
            hasher.HashPassword(new User(), password);
            return null;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }

        return ToSummary(user);
    }

    public async Task<UserSummary?> GetAsync(int userId, CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Name, u.Email, u.Level, u.PronunciationAudioEnabled,
                u.DailyGoal, u.StreakFreezes, u.SoundEnabled, u.ShowInLeagues))
            .FirstOrDefaultAsync(ct);

    /// <summary>Level change affects the next daily round only; today's round keeps its word (AC-05.3).</summary>
    public async Task UpdateSettingsAsync(int userId, UserSettings settings, CancellationToken ct)
    {
        var goal = Math.Clamp(settings.DailyGoal, 1, 20);
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Level, settings.Level)
                .SetProperty(u => u.PronunciationAudioEnabled, settings.PronunciationAudio)
                .SetProperty(u => u.DailyGoal, goal)
                .SetProperty(u => u.SoundEnabled, settings.SoundEnabled)
                .SetProperty(u => u.ShowInLeagues, settings.ShowInLeagues), ct);
    }

    private static UserSummary ToSummary(User u) => new(
        u.Id, u.Name, u.Email, u.Level, u.PronunciationAudioEnabled, u.DailyGoal, u.StreakFreezes, u.SoundEnabled, u.ShowInLeagues);
}
