using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Candle-bucketing logic shared between the live ChartPanel and SimulatedChartPanel — extracted
// verbatim from ChartPanel (same behavior, just relocated) so the simulator can reuse the exact
// same aggregation the live chart uses, instead of a second implementation that could drift.
internal static class CandleAggregation
{
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // rthOnly keeps only 9:30 AM - 4:00 PM ET on each day present in the data (regular session);
    // otherwise keeps everything Schwab returned (regular + pre/after-hours). Not restricted to a
    // single day — covers however many days were requested.
    public static List<CandleData> FilterSession(List<CandleData> candles, bool rthOnly)
    {
        if (!rthOnly) return candles;

        var rthStart = new TimeSpan(9, 30, 0);
        var rthEnd   = new TimeSpan(16, 0, 0);

        return candles
            .Where(c =>
            {
                var eastern = TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone);
                return eastern.TimeOfDay >= rthStart && eastern.TimeOfDay <= rthEnd;
            })
            .ToList();
    }

    // RTH buckets anchor at 9:30 AM ET (matching the regular session open); full-day buckets
    // anchor at midnight ET. Same anchor logic used for both historical batch aggregation and
    // live incremental aggregation, so bucket boundaries always agree.
    public static DateTime BucketAnchor(IEnumerable<CandleData> candles, bool rthOnly) =>
        candles
            .Select(c => TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone))
            .Min(t => rthOnly ? t.Date.AddHours(9).AddMinutes(30) : t.Date);

    public static int BucketIndex(DateTime utcTime, DateTime anchorEastern, int intervalMinutes)
    {
        var eastern   = TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternZone);
        var minutesIn = (eastern - anchorEastern).TotalMinutes;
        return (int)Math.Floor(minutesIn / intervalMinutes);
    }

    // Groups 1-minute candles into fixed-size buckets. Open = first minute's open, Close = last
    // minute's close, High/Low = extremes across the bucket.
    public static List<CandleData> AggregateToInterval(List<CandleData> minuteCandles, int intervalMinutes, bool rthOnly)
    {
        if (minuteCandles.Count == 0) return minuteCandles;

        var anchor = BucketAnchor(minuteCandles, rthOnly);

        return minuteCandles
            .GroupBy(c => BucketIndex(c.Time, anchor, intervalMinutes))
            .OrderBy(g => g.Key)
            .Select(g => BuildCandle(g))
            .ToList();
    }

    // Special-cased 1h bucketing for the Hourly15 panel: the FIRST bucket of the RTH session is
    // only 30 minutes (9:30-10:00, matching the market open), and every bucket after that is
    // aligned to the clock hour (10-11, 11-12, ..., 15-16) instead of floating 60-minute offsets
    // from 9:30 — e.g. a 12:15 tick belongs to the 12:00-13:00 bucket, not an 11:30-12:30 one.
    public static List<CandleData> AggregateToHourlyRthBuckets(List<CandleData> minuteCandles)
    {
        var filtered = FilterSession(minuteCandles, rthOnly: true);
        if (filtered.Count == 0) return filtered;

        return filtered
            .GroupBy(c => HourlyRthBucketKey(c.Time))
            .OrderBy(g => g.Key)
            .Select(g => BuildCandle(g))
            .ToList();
    }

    private static CandleData BuildCandle(IEnumerable<CandleData> bucket)
    {
        var ordered = bucket.OrderBy(c => c.Time).ToList();
        return new CandleData
        {
            Time  = ordered[0].Time,
            Open  = ordered[0].Open,
            Close = ordered[^1].Close,
            High  = ordered.Max(c => c.High),
            Low   = ordered.Min(c => c.Low)
        };
    }

    // slot 0 = the 9:30-10:00 bucket, slot N (10-15) = the N:00-(N+1):00 bucket.
    private static (DateOnly Date, int Slot) HourlyRthBucketOf(DateTime utcTime)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternZone);
        var slot = eastern.TimeOfDay < new TimeSpan(10, 0, 0) ? 0 : eastern.Hour;
        return (DateOnly.FromDateTime(eastern), slot);
    }

    // Unique across arbitrarily many days (DayNumber is a running count since year 1), so grouping
    // by this key never merges the same clock hour across two different days.
    public static long HourlyRthBucketKey(DateTime utcTime)
    {
        var (date, slot) = HourlyRthBucketOf(utcTime);
        return date.DayNumber * 100L + slot;
    }

    // The UTC instant marking the start of the bucket utcTime falls into — used to tag a
    // newly-started live bucket's own Time (9:30 for the first bucket, otherwise slot:00).
    public static DateTime HourlyRthBucketStartUtc(DateTime utcTime)
    {
        var (date, slot) = HourlyRthBucketOf(utcTime);
        var (hour, minute) = slot == 0 ? (9, 30) : (slot, 0);
        var startEastern = date.ToDateTime(new TimeOnly(hour, minute));
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startEastern, DateTimeKind.Unspecified), EasternZone);
    }

    // Groups hourly (or any intraday) candles into one bar per ET calendar day — same rule as
    // chart.html's aggregateToDaily (Open = first bar's open, Close = last bar's close, High/Low
    // = extremes across the day). Used by the daily-bounce-off-SMA20 analysis (ChartPanel), which
    // needs daily bars purely in memory, without touching the WebView/JS side at all.
    public static List<(DateOnly Date, CandleData Candle)> AggregateToDaily(List<CandleData> candles)
    {
        return candles
            .GroupBy(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone)))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, BuildCandle(g)))
            .ToList();
    }
}
