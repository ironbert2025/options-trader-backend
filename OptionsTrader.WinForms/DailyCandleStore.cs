using System.Globalization;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Persists REAL daily candles per symbol (one row per trading day, fetched directly from Yahoo's
// daily-interval endpoint — see scripts/backfill_daily.js), separate from HourlyCandleStore's own
// aggregated-from-hourly daily view. HourlyCandleStore caps at 1500 hourly rows (~214 RTH trading
// days) since Yahoo's 60m granularity itself only goes back ~2 years — nowhere near enough runway
// for SMA100/200 to keep growing. Yahoo's 1d interval has no such practical limit, so this store
// can hold many years of history instead. See ChartPanel.GetLastDailyCandles, which merges this
// with HourlyCandleStore's own (more up-to-date, includes today's still-forming bar) daily
// aggregation for the most recent days.
internal static class DailyCandleStore
{
    private const string OutputFolder = @"C:\OptionsData\MarketData\Candles";
    private const string Header = "Time,Open,High,Low,Close";
    private const int MaxCandles = 3000; // ~12 years — plenty of headroom, cheap to store (1 row/day)

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, $"{symbol}_Daily.csv");

    public static List<CandleData> Load(string symbol)
    {
        var path = PathFor(symbol);
        var result = new List<CandleData>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var open)) continue;
                if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var high)) continue;
                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var low)) continue;
                if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close)) continue;

                result.Add(new CandleData
                {
                    Time  = DateTime.SpecifyKind(time, DateTimeKind.Utc),
                    Open  = open,
                    High  = high,
                    Low   = low,
                    Close = close
                });
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty; the next backfill rebuilds it cleanly.
        }
        return result;
    }

    // Merges in freshCandles, replacing any existing row for the same (Eastern) trading day —
    // same convention as HourlyCandleStore.ReplaceDates, just per-day instead of needing a
    // separate date-vs-slot bucketing since each row already IS one day.
    public static void ReplaceDates(string symbol, List<CandleData> freshCandles)
    {
        if (freshCandles.Count == 0) return;

        Directory.CreateDirectory(OutputFolder);

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        DateOnly EasternDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, eastern));

        var freshDates = freshCandles.Select(c => EasternDate(c.Time)).ToHashSet();
        var existing = Load(symbol).Where(c => !freshDates.Contains(EasternDate(c.Time)));

        var merged = existing
            .Concat(freshCandles)
            .OrderBy(c => c.Time)
            .ToList();

        if (merged.Count > MaxCandles)
            merged = merged.Skip(merged.Count - MaxCandles).ToList();

        using var writer = new StreamWriter(PathFor(symbol), append: false);
        writer.WriteLine(Header);
        foreach (var c in merged)
            writer.WriteLine($"{c.Time:O},{c.Open},{c.High},{c.Low},{c.Close}");
    }
}
