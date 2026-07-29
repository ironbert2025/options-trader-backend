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
            .Select(g =>
            {
                var ordered = g.OrderBy(c => c.Time).ToList();
                return new CandleData
                {
                    Time  = ordered[0].Time,
                    Open  = ordered[0].Open,
                    Close = ordered[^1].Close,
                    High  = ordered.Max(c => c.High),
                    Low   = ordered.Min(c => c.Low)
                };
            })
            .ToList();
    }
}
