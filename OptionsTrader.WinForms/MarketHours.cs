namespace OptionsTrader.WinForms;

/// <summary>
/// Provides NYSE market hours logic (9:30 AM - 4:00 PM EST).
/// </summary>
public static class MarketHours
{
    private static readonly TimeZoneInfo EstZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static readonly TimeOnly MarketOpen  = new(9, 30);
    private static readonly TimeOnly MarketClose = new(16, 0);

    public static DateTime NowEst => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EstZone);

    public static bool IsOpen
    {
        get
        {
            var now = NowEst;
            var time = TimeOnly.FromDateTime(now);
            return now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                && time >= MarketOpen
                && time < MarketClose;
        }
    }

    /// <summary>
    /// Returns how many milliseconds until the next market open.
    /// Returns 0 if market is currently open.
    /// </summary>
    public static double MillisecondsUntilOpen
    {
        get
        {
            if (IsOpen) return 0;

            var now = NowEst;
            var today = DateOnly.FromDateTime(now);
            var nextOpen = NextOpenDateTime(today, now);
            return (nextOpen - now).TotalMilliseconds;
        }
    }

    private static DateTime NextOpenDateTime(DateOnly date, DateTime now)
    {
        // Find next weekday market open
        for (int i = 0; i <= 7; i++)
        {
            var candidate = date.AddDays(i);
            if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var openDt = new DateTime(candidate.Year, candidate.Month, candidate.Day,
                MarketOpen.Hour, MarketOpen.Minute, 0);

            if (openDt > now)
                return openDt;
        }

        // Fallback: next Monday
        var next = date.AddDays(1);
        return new DateTime(next.Year, next.Month, next.Day, MarketOpen.Hour, MarketOpen.Minute, 0);
    }
}
