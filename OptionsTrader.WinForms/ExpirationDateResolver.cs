namespace OptionsTrader.WinForms;

/// <summary>
/// Resolves the options expiration date based on the ExpDate code stored in Tickers settings.
/// Supported codes:
///   D   = Daily   → today's date
///   F   = Friday  → this week's Friday (or today if today is Friday)
///   MWF = Mon/Wed/Fri → the next occurrence of Monday, Wednesday, or Friday (inclusive of today)
/// </summary>
public static class ExpirationDateResolver
{
    public static DateOnly Resolve(string expDateCode)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return expDateCode.Trim().ToUpperInvariant() switch
        {
            "D"   => today,
            "F"   => NextWeekday(today, DayOfWeek.Friday),
            "MWF" => NextMwf(today),
            _     => DateOnly.TryParse(expDateCode, out var parsed) ? parsed : today
        };
    }

    // Resolves the expiration date that comes right after the current one (e.g. tomorrow for "D").
    // Fixed dates have no "next" occurrence, so they resolve to the same date.
    public static DateOnly ResolveNext(string expDateCode)
    {
        var current = Resolve(expDateCode);
        var dayAfterCurrent = current.AddDays(1);

        return expDateCode.Trim().ToUpperInvariant() switch
        {
            "D"   => dayAfterCurrent,
            "F"   => NextWeekday(dayAfterCurrent, DayOfWeek.Friday),
            "MWF" => NextMwf(dayAfterCurrent),
            _     => current
        };
    }

    // Returns today if today matches the target day, otherwise the next occurrence.
    private static DateOnly NextWeekday(DateOnly from, DayOfWeek target)
    {
        int daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(daysUntil);
    }

    // Returns today if today is Mon/Wed/Fri, otherwise the next Mon/Wed/Fri.
    private static DateOnly NextMwf(DateOnly from)
    {
        for (int i = 0; i <= 7; i++)
        {
            var candidate = from.AddDays(i);
            if (candidate.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
                return candidate;
        }
        return from; // fallback (should never reach here)
    }
}
