namespace OptionsTrader.WinForms;

// What happens after a band touch closes a validation cycle, before the SAME direction may validate
// again. "Immediate" is the literal rule ("se rearma una vez el toque"); the others exist because
// real L1 data showed 50-70% of immediate re-validations land within 30 s of the previous touch
// (price chattering at the band edge) — calibrated offline, see the quick-scan button.
internal enum BbRearmPolicy
{
    Immediate,      // re-arm on the very next tick
    CoolDown60s,    // re-arm 60 s after the touch
    NextCandle,     // re-arm when the next 15m candle starts
    BackToMidline   // re-arm once spot has returned to the Punto Medio
}

internal enum BbMomentumEventKind { Validation, Touch, Lost }

// TimeUtc is the TICK's own time (sub-second), Bullish=false means the mirrored downward setup.
internal sealed record BbMomentumEvent(DateTime TimeUtc, BbMomentumEventKind Kind, bool Bullish, decimal Spot);

// Detects, tick by tick, a Bollinger(20, 2) momentum setup on 15m RTH candles:
//   Bullish: Punto Medio (SMA20) AND upper band both sloping UP versus the previous COMPLETED
//            candle's value, spot strictly between Punto Medio and upper band, and spot moving up
//            (above its price MovingWindow ago).  Bearish: the exact mirror with the lower band.
// Cycle per direction: Idle -> (all of the above true) Validation -> (spot reaches the band) Touch ->
// cycle closed, re-armed per BbRearmPolicy. If the structure (slopes + "spot between") stops holding
// before the touch, a single Lost event is raised for that cycle and the cycle stays open — nothing is
// cancelled, per explicit request; the touch still closes it.
//
// Band math is the same as the chart's (chart.html recalculateBollinger): population standard
// deviation, the FORMING candle's close = current spot, "previous" = the bands ending at the last
// completed candle (yesterday's last candle for the first bucket of the day). Pure logic with no UI
// dependency on purpose, so the live Charts tab can reuse it later.
//
// Feed it every tick of ONE day's RTH session in chronological order (OnTick ignores anything
// outside 9:30-16:00 ET); seed it with the closes of the already-completed prior-day RTH candles.
internal sealed class BollingerMomentumDetector
{
    private const int Period = 20;
    private const double Mult = 2.0;
    private static readonly TimeSpan RthStart = new(9, 30, 0);
    private static readonly TimeSpan RthEnd = new(16, 0, 0);
    private const double BucketMinutes = 15.0;
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly IReadOnlyList<BbMomentumEvent> NoEvents = Array.Empty<BbMomentumEvent>();

    private readonly BbRearmPolicy _rearm;
    private readonly TimeSpan _movingWindow;
    private readonly List<double> _closes;                 // COMPLETED candle closes, chronological
    private readonly LinkedList<(DateTime T, double P)> _recent = new(); // last tick <= now-window stays at the head

    private int _formingBucket = -1;
    private double _formingLast;
    private bool _hasPrev;
    private double _midPrev, _upPrev, _loPrev, _s19, _q19;

    private sealed class Cycle
    {
        public bool Pending;
        public bool LostLogged;
        public bool Locked;
        public int LockBucket;
        public DateTime LockTime;
    }
    private readonly Cycle _bull = new();
    private readonly Cycle _bear = new();

    public BollingerMomentumDetector(IEnumerable<decimal> priorCompletedCloses, BbRearmPolicy rearm, TimeSpan movingWindow)
    {
        _closes = priorCompletedCloses.Select(c => (double)c).ToList();
        _rearm = rearm;
        _movingWindow = movingWindow;
        RefreshPrev();
    }

    private void RefreshPrev()
    {
        if (_closes.Count < Period)
        {
            _hasPrev = false;
            return;
        }
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

    public IReadOnlyList<BbMomentumEvent> OnTick(DateTime timeUtc, decimal price)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(timeUtc, Eastern);
        var tod = eastern.TimeOfDay;
        if (tod < RthStart || tod >= RthEnd) return NoEvents;

        var spot = (double)price;
        var bucket = (int)Math.Floor((tod - RthStart).TotalMinutes / BucketMinutes);
        if (_formingBucket == -1)
        {
            _formingBucket = bucket;
        }
        else if (bucket != _formingBucket)
        {
            _closes.Add(_formingLast);      // the forming candle just completed
            _formingBucket = bucket;
            RefreshPrev();
        }
        _formingLast = spot;

        // Price ~MovingWindow ago: the last tick at or before (now - window). The head of the list is
        // advanced while the NEXT entry is still old enough, so the head is always that tick.
        _recent.AddLast((timeUtc, spot));
        var cutoff = timeUtc - _movingWindow;
        while (_recent.First!.Next != null && _recent.First.Next.Value.T <= cutoff) _recent.RemoveFirst();
        double? ago = _recent.First.Value.T <= cutoff ? _recent.First.Value.P : null;

        if (!_hasPrev) return NoEvents;

        var mid = (_s19 + spot) / Period;
        var sd = Math.Sqrt(Math.Max((_q19 + spot * spot) / Period - mid * mid, 0.0));
        var up = mid + Mult * sd;
        var lo = mid - Mult * sd;

        List<BbMomentumEvent>? events = null;
        void Add(BbMomentumEventKind kind, bool bullish) =>
            (events ??= new()).Add(new BbMomentumEvent(timeUtc, kind, bullish, price));

        // ---- bullish ----
        var bullStruct = mid > _midPrev && up > _upPrev && mid < spot && spot < up;
        var bullMove = ago.HasValue && spot > ago.Value;
        Step(_bull, bullish: true, bullStruct, bullMove, touched: spot >= up, backAtMid: spot <= mid, bucket, timeUtc, Add);

        // ---- bearish (mirror) ----
        var bearStruct = mid < _midPrev && lo < _loPrev && lo < spot && spot < mid;
        var bearMove = ago.HasValue && spot < ago.Value;
        Step(_bear, bullish: false, bearStruct, bearMove, touched: spot <= lo, backAtMid: spot >= mid, bucket, timeUtc, Add);

        return events ?? NoEvents;
    }

    private void Step(Cycle c, bool bullish, bool structure, bool moving, bool touched, bool backAtMid,
        int bucket, DateTime timeUtc, Action<BbMomentumEventKind, bool> add)
    {
        if (!c.Pending)
        {
            if (c.Locked)
            {
                switch (_rearm)
                {
                    case BbRearmPolicy.BackToMidline when backAtMid: c.Locked = false; break;
                    case BbRearmPolicy.NextCandle when bucket != c.LockBucket: c.Locked = false; break;
                    case BbRearmPolicy.CoolDown60s when (timeUtc - c.LockTime).TotalSeconds >= 60: c.Locked = false; break;
                }
                if (c.Locked) return;
            }
            if (structure && moving)
            {
                c.Pending = true;
                c.LostLogged = false;
                add(BbMomentumEventKind.Validation, bullish);
            }
            return;
        }

        if (touched)
        {
            // Checked BEFORE "lost": reaching the band itself also breaks "spot between", and that is
            // the cycle closing, not a loss.
            add(BbMomentumEventKind.Touch, bullish);
            c.Pending = false;
            if (_rearm != BbRearmPolicy.Immediate)
            {
                c.Locked = true;
                c.LockBucket = bucket;
                c.LockTime = timeUtc;
            }
        }
        else if (!structure && !c.LostLogged)
        {
            c.LostLogged = true;
            add(BbMomentumEventKind.Lost, bullish);
        }
    }
}
