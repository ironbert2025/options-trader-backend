using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists vertical arrow drawings on the 1h chart panel, per symbol, so they reappear at the
// same point after closing and reopening the app. Times are the same "ET digits disguised as
// UTC" epoch seconds chart.html already works in (see ChartPanel.ToChartJson) — stored and
// round-tripped as opaque longs here, never interpreted as a real UTC instant on the C# side.
internal static class VerticalArrowStore
{
    private const string OutputFolder = @"C:\OptionsData\ChartDrawings";
    private const string Header = "Time,Price,Up";

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, symbol, $"{symbol}_Arrows.csv");

    public static List<(long Time, decimal Price, bool Up)> Load(string symbol)
    {
        var path = PathFor(symbol);
        var result = new List<(long, decimal, bool)>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 3) continue;
                if (!long.TryParse(parts[0], out var time)) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) continue;
                if (!bool.TryParse(parts[2], out var up)) continue;
                result.Add((time, price, up));
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty; the next Append rebuilds it cleanly.
        }
        return result;
    }

    public static void Append(string symbol, long time, decimal price, bool up)
    {
        var path = PathFor(symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var isNew = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (isNew) writer.WriteLine(Header);
        writer.WriteLine($"{time},{price.ToString(CultureInfo.InvariantCulture)},{up}");
    }

    // Removes the first stored arrow whose 3 values exactly match (identifies an arrow since it
    // has no separate id) — used when the user deletes one via the Delete key.
    public static void Remove(string symbol, long time, decimal price, bool up)
    {
        var existing = Load(symbol);
        var idx = existing.FindIndex(a => a.Time == time && a.Price == price && a.Up == up);
        if (idx == -1) return;
        existing.RemoveAt(idx);
        Rewrite(symbol, existing);
    }

    // Updates the first stored arrow matching the OLD position/direction to the NEW position —
    // used when the user drags an arrow to a new spot.
    public static void Move(string symbol, long oldTime, decimal oldPrice, bool up, long newTime, decimal newPrice)
    {
        var existing = Load(symbol);
        var idx = existing.FindIndex(a => a.Time == oldTime && a.Price == oldPrice && a.Up == up);
        if (idx == -1) return;
        existing[idx] = (newTime, newPrice, up);
        Rewrite(symbol, existing);
    }

    public static void Clear(string symbol)
    {
        var path = PathFor(symbol);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void Rewrite(string symbol, List<(long Time, decimal Price, bool Up)> rows)
    {
        var path = PathFor(symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(Header);
        foreach (var r in rows)
            writer.WriteLine($"{r.Time},{r.Price.ToString(CultureInfo.InvariantCulture)},{r.Up}");
    }
}
