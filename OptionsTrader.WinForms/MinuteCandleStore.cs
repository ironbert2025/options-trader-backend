using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Where a 15-minute bucket stands, for the candle-detail window: still being drawn, just closed but its
// last 1-minute bar has not been confirmed yet (the bar of minute 9:59 is delivered after 10:00), or done.
internal enum MinuteBucketState { Forming, Settling, Closed }

// 1-minute candles of the live session, built from Level 1 ticks (the tick's own trade time decides its
// minute) and corrected by the official CHART_EQUITY bar once that arrives (the bar replaces the minute
// outright and is marked official — later ticks of that minute are ignored). Pure logic, no UI.
internal sealed class MinuteCandleStore
{
    private const int MaxMinutes = 3000; // ~7.5 RTH sessions
    private readonly object _lock = new();
    private readonly SortedDictionary<DateTime, Entry> _minutes = new();

    private sealed class Entry
    {
        public required CandleData Candle { get; init; }
        public bool Official { get; set; }
    }

    public static DateTime MinuteStart(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    public void OnTick(DateTime utc, decimal price)
    {
        var minute = MinuteStart(utc);
        lock (_lock)
        {
            if (_minutes.TryGetValue(minute, out var entry))
            {
                if (entry.Official) return; // the official bar already settled this minute
                entry.Candle.High = Math.Max(entry.Candle.High, price);
                entry.Candle.Low = Math.Min(entry.Candle.Low, price);
                entry.Candle.Close = price;
                return;
            }
            _minutes[minute] = new Entry
            {
                Candle = new CandleData { Time = minute, Open = price, High = price, Low = price, Close = price }
            };
            while (_minutes.Count > MaxMinutes) _minutes.Remove(_minutes.Keys.First());
        }
    }

    // The 1-minute CHART_EQUITY bar: official OHLC for the minute starting at bar.Time.
    public void OnBar(CandleData bar)
    {
        var minute = MinuteStart(bar.Time);
        lock (_lock)
        {
            _minutes[minute] = new Entry
            {
                Candle = new CandleData { Time = minute, Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close },
                Official = true
            };
            while (_minutes.Count > MaxMinutes) _minutes.Remove(_minutes.Keys.First());
        }
    }

    public bool IsOfficial(DateTime minuteStartUtc)
    {
        lock (_lock) return _minutes.TryGetValue(minuteStartUtc, out var e) && e.Official;
    }

    // Minutes in [startUtc, endUtc): the history's candles (already official) with this store's minutes
    // laid over them, ordered by time. Copies, so a caller can hold them while ticks keep coming.
    public List<CandleData> GetRange(DateTime startUtc, DateTime endUtc, IEnumerable<CandleData>? history)
    {
        var byMinute = new SortedDictionary<DateTime, CandleData>();
        if (history != null)
        {
            foreach (var c in history)
            {
                var minute = MinuteStart(c.Time);
                if (minute >= startUtc && minute < endUtc)
                    byMinute[minute] = new CandleData { Time = minute, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume };
            }
        }
        lock (_lock)
        {
            foreach (var (minute, entry) in _minutes)
            {
                if (minute < startUtc || minute >= endUtc) continue;
                var c = entry.Candle;
                byMinute[minute] = new CandleData { Time = minute, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close };
            }
        }
        return byMinute.Values.ToList();
    }

    // 1-minute candles from raw tick-candles (each one a single price, or its own tiny OHLC), for the
    // Simulator: ticks in [startUtc, min(endUtc, uptoUtc)] ordered by time.
    public static List<CandleData> FromTicks(IReadOnlyList<CandleData> ticks, DateTime startUtc, DateTime endUtc, DateTime uptoUtc)
    {
        var result = new List<CandleData>();
        if (ticks.Count == 0) return result;

        // first tick at/after startUtc
        int lo = 0, hi = ticks.Count - 1, first = ticks.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (ticks[mid].Time >= startUtc) { first = mid; hi = mid - 1; } else lo = mid + 1;
        }

        CandleData? current = null;
        for (var i = first; i < ticks.Count; i++)
        {
            var t = ticks[i];
            if (t.Time >= endUtc || t.Time > uptoUtc) break;
            var minute = MinuteStart(t.Time);
            if (current == null || current.Time != minute)
            {
                current = new CandleData { Time = minute, Open = t.Open, High = t.High, Low = t.Low, Close = t.Close };
                result.Add(current);
            }
            else
            {
                current.High = Math.Max(current.High, t.High);
                current.Low = Math.Min(current.Low, t.Low);
                current.Close = t.Close;
            }
        }
        return result;
    }
}
