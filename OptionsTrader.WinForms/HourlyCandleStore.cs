using System.Globalization;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Persists 1h candles per symbol across sessions so the SMA 100/200 overlay on the Hourly15 chart
// panel can eventually accumulate enough history — Schwab's pricehistory endpoint only returns
// 10 days per fetch, nowhere near the 200 candles SMA 200 needs. Each session's fetch gets merged
// into whatever was already saved, so the file grows by roughly one trading day per session.
internal static class HourlyCandleStore
{
    private const string OutputFolder = @"C:\OptionsData";
    private const string Header = "Time,Open,High,Low,Close";
    private const int MaxCandles = 300; // headroom above 200 without growing forever

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, $"{symbol}_Hourly1h.csv");

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
            // Corrupt/partial file — treat as empty; the next AppendIfMissing rebuilds it cleanly.
        }
        return result;
    }

    // Merges in any candle whose Time isn't already stored, then trims to the most recent
    // MaxCandles. Safe to call every session — already-known candles are simply skipped.
    public static void AppendIfMissing(string symbol, List<CandleData> candles)
    {
        if (candles.Count == 0) return;

        Directory.CreateDirectory(OutputFolder);

        var existing = Load(symbol);
        var existingTimes = existing.Select(c => c.Time).ToHashSet();

        var merged = existing
            .Concat(candles.Where(c => !existingTimes.Contains(c.Time)))
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
