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

    // Morning cutoff for the optional "Stop at 11:00 AM" toggle.
    public static readonly TimeOnly StopAt11 = new(11, 0);

    // Start of the end-of-day auto-capture window (5 min before close).
    public static readonly TimeOnly AutoCaptureStart = new(15, 55);

    public static DateTime NowEst => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EstZone);

    public static bool IsWeekday =>
        NowEst.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    // True once it is 11:00 AM EST or later on a weekday.
    public static bool IsPastStopTime
    {
        get
        {
            var time = TimeOnly.FromDateTime(NowEst);
            return IsWeekday && time >= StopAt11;
        }
    }

    // True during the final capture window (3:55 PM – 4:00 PM EST) on a weekday.
    public static bool IsInAutoCaptureWindow
    {
        get
        {
            var time = TimeOnly.FromDateTime(NowEst);
            return IsWeekday && time >= AutoCaptureStart && time < MarketClose;
        }
    }

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
