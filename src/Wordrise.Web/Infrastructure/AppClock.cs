using Microsoft.Extensions.Options;

namespace Wordrise.Web.Infrastructure;

/// <summary>Single source of "today" for the game, computed in the configured time zone.</summary>
public interface IAppClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset LocalNow { get; }
    DateOnly Today { get; }

    /// <summary>Time left until the next daily word (local midnight).</summary>
    TimeSpan UntilNextDay { get; }
}

public sealed class AppClock : IAppClock
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _zone;

    public AppClock(TimeProvider timeProvider, IOptions<GameOptions> options)
    {
        _timeProvider = timeProvider;
        _zone = ResolveTimeZone(options.Value.TimeZone);
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, _zone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);

    public TimeSpan UntilNextDay
    {
        get
        {
            var now = UtcNow;
            var nextLocalMidnight = Today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, _zone);
            var left = new DateTimeOffset(nextUtc, TimeSpan.Zero) - now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    internal static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        // Windows without ICU IANA support: try the Windows id equivalent.
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone))
        {
            return zone;
        }

        throw new InvalidOperationException($"Time zone '{id}' was not found on this system (is tzdata installed?).");
    }
}
