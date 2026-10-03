using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists the DZ/SZ (Demand Zone / Supply Zone) tool's drawings per symbol AND context (tag) —
// used by DailyChartForm, one tag per tab ("Daily"/"DailyHora"/"Daily15Min") so a zone drawn on
// one tab never bleeds into another. Unlike ChartPanel/SimulatedChartPanel's own DZ/SZ (which also
// does rebote tracking + cross-chart mirroring), this store is pure draw+persist — each line of a
// demand/supply pair is stored individually (time, price, color), same shape chart.html's
// addMirroredZoneLine replay function already expects. Same file-format convention as
// RectStore/TLineStore. Times are the same "ET digits disguised as UTC" epoch seconds chart.html
// already works in — stored and round-tripped as opaque longs here.
internal static class ZoneStore
{
    private const string OutputFolder = @"C:\OptionsData\ChartDrawings";
    private const string Header = "Time,Price,Color";

    private static string PathFor(string symbol, string tag) => Path.Combine(OutputFolder, symbol, $"{symbol}_Zones_{tag}.csv");

    public static List<(long Time, decimal Price, string Color)> Load(string symbol, string tag)
    {
        var path = PathFor(symbol, tag);
        var result = new List<(long, decimal, string)>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 3) continue;
                if (!long.TryParse(parts[0], out var t)) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) continue;
                result.Add((t, p, parts[2]));
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty; the next Append rebuilds it cleanly.
        }
        return result;
    }

    public static void Append(string symbol, string tag, long time, decimal price, string color)
    {
        var path = PathFor(symbol, tag);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var isNew = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (isNew) writer.WriteLine(Header);
        writer.WriteLine($"{time},{price.ToString(CultureInfo.InvariantCulture)},{color}");
    }

    // Removes the stored line matching this price (used when the user deletes a whole zone pair —
    // called once per price1/price2). Matched by price only, same convention HLineStore uses,
    // since a zone line's price is effectively its identity once drawn.
    public static void Remove(string symbol, string tag, decimal price)
    {
        var path = PathFor(symbol, tag);
        var existing = Load(symbol, tag);
        var idx = existing.FindIndex(r => r.Price == price);
        if (idx == -1) return;
        existing.RemoveAt(idx);

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(Header);
        foreach (var r in existing)
            writer.WriteLine($"{r.Time},{r.Price.ToString(CultureInfo.InvariantCulture)},{r.Color}");
    }
}
