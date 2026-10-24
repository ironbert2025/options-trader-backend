namespace OptionsTrader.WinForms;

// A directional entry setup on the 15m RTH Bollinger(20, 2) bands. Bullish = CALL setup, otherwise PUT.
internal sealed record BbSetupEvent(DateTime TimeUtc, bool Bullish, decimal Spot, double Mid, double Up, double Lo,
    double MidPrev, double UpPrev, double LoPrev)
{
    // Where the spot sits relative to the middle band when the setup fires (informative only).
    public bool SpotAboveMid => (double)Spot > Mid;
}

// Detects, tick by tick, the setup to enter a PUT (or, mirrored, a CALL):
//   PUT : spot INSIDE the bands, spot moving DOWN (below its price MovingWindow ago), the middle band (SMA20) below its
//         value at the previous completed candle (sloping down, or just starting to), and AT LEAST ONE band opening
//         (the upper one higher, or the lower one lower, than at the previous completed candle).
//   CALL: spot inside the bands, moving UP, the middle band above the previous candle's, and at least one band opening.
// "Previous" = the bands ending at the last completed candle; the forming candle's close is the live spot — the same
// band math and conventions as BollingerMomentumDetector (population standard deviation, yesterday's last candle for
// the first bucket of the day). Fires once per 15m candle and direction, the moment every condition holds; the same
// direction can fire again from the next candle on. Pure logic, no UI: the Simulator feeds it first, the live Charts
// tab can reuse it. Feed it every tick of the RTH session in chronological order; seed it with the closes of the
// already-completed prior-day RTH candles.
internal sealed class BbDirectionalSetupDetector
{
    private const int Period = 20;
    private const double Mult = 2.0;
    private static readonly TimeSpan RthStart = new(9, 30, 0);
    private static readonly TimeSpan RthEnd = new(16, 0, 0);
    private const double BucketMinutes = 15.0;
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private readonly TimeSpan _movingWindow;
    private readonly List<double> _closes;
    private readonly LinkedList<(DateTime T, double P)> _recent = new();

    private DateOnly? _lastDate;
    private int _formingBucket = -1;
    private double _formingLast;
    private bool _hasPrev;
    private double _midPrev, _upPrev, _loPrev, _s19, _q19;
    private int _bullLockBucket = -1, _bearLockBucket = -1; // candle in which that direction already fired

    public BbDirectionalSetupDetector(IEnumerable<decimal> priorCompletedCloses, TimeSpan movingWindow)
    {
        _closes = priorCompletedCloses.Select(c => (double)c).ToList();
        _movingWindow = movingWindow;
        RefreshPrev();
    }

    private void RefreshPrev()
    {
        if (_closes.Count < Period) { _hasPrev = false; return; }
        var w20 = _closes.GetRange(_closes.Count - Period, Period);
        var mean = w20.Average();
        var sd = Math.Sqrt(w20.Sum(x => (x - mean) * (x - mean)) / Period);
        _midPrev = mean;
        _upPrev = mean + Mult * sd;
        _loPrev = mean - Mult * sd;
        var w19 = _closes.GetRange(_closes.Count - (Period - 1), Period - 1);
        _s19 = w19.Sum();
        _q19 = w19.Sum(x => x * x);
        _hasPrev = true;
    }

    public BbSetupEvent? OnTick(DateTime timeUtc, decimal price)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(timeUtc, Eastern);
        var tod = eastern.TimeOfDay;
        if (tod < RthStart || tod >= RthEnd) return null;

        var date = DateOnly.FromDateTime(eastern);
        if (_lastDate.HasValue && date != _lastDate.Value)
        {
            _recent.Clear();
            _bullLockBucket = _bearLockBucket = -1;
        }
        _lastDate = date;

        var spot = (double)price;
        var bucket = (int)Math.Floor((tod - RthStart).TotalMinutes / BucketMinutes);
        if (_formingBucket == -1) _formingBucket = bucket;
        else if (bucket != _formingBucket)
        {
            _closes.Add(_formingLast); // the forming candle just completed
            _formingBucket = bucket;
            RefreshPrev();
        }
        _formingLast = spot;

        _recent.AddLast((timeUtc, spot));
        var cutoff = timeUtc - _movingWindow;
        while (_recent.First!.Next != null && _recent.First.Next.Value.T <= cutoff) _recent.RemoveFirst();
        double? ago = _recent.First.Value.T <= cutoff ? _recent.First.Value.P : null;

        if (!_hasPrev || !ago.HasValue) return null;

        var mid = (_s19 + spot) / Period;
        var sd = Math.Sqrt(Math.Max((_q19 + spot * spot) / Period - mid * mid, 0.0));
        var up = mid + Mult * sd;
        var lo = mid - Mult * sd;
        if (!(lo < spot && spot < up)) return null;          // spot must be inside the bands
        if (!(up > _upPrev || lo < _loPrev)) return null;    // at least one band opening

        var bullish = spot > ago.Value && mid > _midPrev;     // moving up + middle band rising  -> CALL
        var bearish = spot < ago.Value && mid < _midPrev;     // moving down + middle band falling -> PUT
        if (bullish && _bullLockBucket != bucket)
        {
            _bullLockBucket = bucket;
            return new BbSetupEvent(timeUtc, true, price, mid, up, lo, _midPrev, _upPrev, _loPrev);
        }
        if (bearish && _bearLockBucket != bucket)
        {
            _bearLockBucket = bucket;
            return new BbSetupEvent(timeUtc, false, price, mid, up, lo, _midPrev, _upPrev, _loPrev);
        }
        return null;
    }
}
