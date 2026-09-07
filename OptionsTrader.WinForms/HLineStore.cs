using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists the H-Line drawn on DailyChartForm's "Hora"/"15 Min" tabs — a SINGLE canonical line per
// symbol (not split by tab like TLineStore), since drawing it on either tab mirrors it onto the
// other one and both stay in sync (see DailyChartForm.OnHLineDrawnEvent/OnHLineDeletedEvent).
// Times are the same "ET digits disguised as UTC" epoch seconds chart.html already works in (see
// ChartPanel.ToChartJson) — stored and round-tripped as opaque longs here, never interpreted as a
// real UTC instant on the C# side.
internal static class HLineStore
{
    private const string OutputFolder = @"C:\OptionsData\ChartDrawings";
    private const string Header = "Time,Price";

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, symbol, $"{symbol}_HLines_Daily.csv");

    public static List<(long Time, decimal Price)> Load(string symbol)
    {
        var path = PathFor(symbol);
        var result = new List<(long, decimal)>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 2) continue;
                if (!long.TryParse(parts[0], out var time)) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) continue;
                result.Add((time, price));
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty; the next Append rebuilds it cleanly.
        }
        return result;
    }

    public static void Append(string symbol, long time, decimal price)
    {
        var path = PathFor(symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var isNew = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (isNew) writer.WriteLine(Header);
        writer.WriteLine($"{time},{price.ToString(CultureInfo.InvariantCulture)}");
    }

    // Matches by price with a small tolerance — same reasoning as chart.html's own removeHLine —
    // the incoming price came from a pixel->price conversion with many more floating-point digits
    // than what got persisted (and round-tripped) here.
    public static void Remove(string symbol, decimal price)
    {
        var path = PathFor(symbol);
        var existing = Load(symbol);
        var idx = existing.FindIndex(l => Math.Abs(l.Price - price) < 0.005m);
        if (idx == -1) return;
        existing.RemoveAt(idx);

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(Header);
        foreach (var l in existing)
            writer.WriteLine($"{l.Time},{l.Price.ToString(CultureInfo.InvariantCulture)}");
    }
}
