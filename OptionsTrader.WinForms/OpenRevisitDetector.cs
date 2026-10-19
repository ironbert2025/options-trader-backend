namespace OptionsTrader.WinForms;

// What the detector reports: the first 15m candle of the session moved away from its open (the wick), came back
// through it, and the body (open -> price) is now at least BodyRatio of that wick, on the OTHER side of the open.
// BodyUp: the body is above the open (the wick hangs below it); false: body below, the wick is above.
internal sealed record OpenRevisitEvent(DateTime TimeUtc, bool BodyUp, decimal Open, decimal Wick, decimal Body, decimal Price);

// "Open revisit" in the first 15-minute candle (09:30–09:45 ET): the price leaves the open, comes back through it
// and keeps going the other way. Tick fed (Level 1 ticks, or the Simulator's replay ticks), pure logic, no UI.
//
// Rule (calibrated over 50–53 days of saved ticks per symbol, see scripts/calibrate_open_revisit.py):
//   wick = largest distance from the open on the side opposite to where the price is now
//   body = distance from the open to the current price (on the other side)
//   fires when wick >= WickFloor and body >= BodyRatio * wick — once per session.
// WickFloor is per symbol: FloorFraction (0.15) x the median range of the first 15m candle of the previous days,
// so it follows each symbol's own volatility without a percentage of the price.
internal sealed class OpenRevisitDetector
{
    public const decimal BodyRatio = 0.4m;
    public const decimal FloorFraction = 0.15m;
    private static readonly TimeSpan SessionStart = new(9, 30, 0);
    private static readonly TimeSpan SessionEnd = new(9, 45, 0);
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private readonly decimal _wickFloor;
    private DateOnly _day;
    private bool _haveOpen;
    private decimal _open, _high, _low;
    private bool _fired;
    private bool _lastBodyUp;         // direction of the last alert
    private bool _oppositeAllowed;    // the user deleted that alert: the opposite cross may be reported once

    // The user deleted the alert's line from the chart: a cross the OTHER way (once) is reported again. Without that
    // deletion nothing more is reported for the session.
    public void AllowOppositeAfterDelete()
    {
        if (_fired) _oppositeAllowed = true;
    }

    public decimal WickFloor => _wickFloor;
    public bool HasOpen => _haveOpen;
    public decimal Open => _open;

    // medianFirstCandleRange: median High-Low of the first 15m RTH candle of up to the last 20 previous days.
    public OpenRevisitDetector(decimal medianFirstCandleRange) => _wickFloor = FloorFraction * medianFirstCandleRange;

    // Median of the first-candle ranges (null when there are too few days to trust it).
    public static decimal? MedianRange(IReadOnlyList<decimal> ranges, int minDays = 5)
    {
        if (ranges.Count < minDays) return null;
        var sorted = ranges.OrderBy(r => r).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    public OpenRevisitEvent? OnTick(DateTime utc, decimal price)
    {
        if (price <= 0) return null;
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utc, EasternZone);
        var day = DateOnly.FromDateTime(eastern);
        if (day != _day) { _day = day; _haveOpen = false; _fired = false; _oppositeAllowed = false; }

        var tod = eastern.TimeOfDay;
        if (tod < SessionStart || tod >= SessionEnd) return null;

        if (!_haveOpen)
        {
            _haveOpen = true;
            _open = _high = _low = price;
            return null;
        }
        _high = Math.Max(_high, price);
        _low = Math.Min(_low, price);

        return Evaluate(utc, _open, _high, _low, price);
    }

    // Same rule, read straight from the candle as drawn (open/high/low and the current price as its close) — what the
    // live chart uses, so the alert always agrees with what is on screen. Only the first 15m candle counts.
    public OpenRevisitEvent? OnCandle(DateTime utc, decimal open, decimal high, decimal low, decimal close)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utc, EasternZone);
        var day = DateOnly.FromDateTime(eastern);
        if (day != _day) { _day = day; _haveOpen = false; _fired = false; _oppositeAllowed = false; }
        var tod = eastern.TimeOfDay;
        if (tod < SessionStart || tod >= SessionEnd || open <= 0) return null;
        return Evaluate(utc, open, high, low, close);
    }

    private OpenRevisitEvent? Evaluate(DateTime utc, decimal open, decimal high, decimal low, decimal price)
    {
        // Body above the open -> the wick is the excursion below it; body below -> the excursion above.
        var bodyUp = price > open;
        var body = Math.Abs(price - open);
        var wick = bodyUp ? open - low : high - open;
        if (_wickFloor <= 0 || body <= 0 || wick < _wickFloor || body < BodyRatio * wick) return null;
        if (_fired && !(_oppositeAllowed && bodyUp != _lastBodyUp)) return null; // already reported, unless deleted and now the other way
        _fired = true;
        _lastBodyUp = bodyUp;
        _oppositeAllowed = false;
        return new OpenRevisitEvent(utc, bodyUp, open, wick, body, price);
    }
}
