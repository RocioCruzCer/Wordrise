namespace Wordrise.Web.Infrastructure;

public static class Humanize
{
    public static string DaysAgo(DateOnly date, DateOnly today)
    {
        var days = today.DayNumber - date.DayNumber;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            _ => $"{days} days ago"
        };
    }
}
