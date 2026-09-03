using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists the BLUE rectangle tool's drawings per symbol AND context (contextTag) — used by
// DailyChartForm's "Daily" tab (contextTag "Daily") so it doesn't collide with anything else that
// might reuse the same blue rect tool later. Deliberately separate from RectGrisStore (the GRAY
// rect tool, single file per symbol, no context split) — same file-format convention as
// TLineStore, just for rectangles instead of lines. Times are the same "ET digits disguised as
// UTC" epoch seconds chart.html already works in — stored and round-tripped as opaque longs here.
internal static class RectStore
{
    private const string OutputFolder = @"C:\OptionsData\ChartDrawings";
    private const string Header = "Time1,Price1,Time2,Price2";

    private static string PathFor(string symbol, string contextTag) => Path.Combine(OutputFolder, symbol, $"{symbol}_Rects_{contextTag}.csv");

    public static List<(long T1, decimal P1, long T2, decimal P2)> Load(string symbol, string contextTag)
    {
        var path = PathFor(symbol, contextTag);
        var result = new List<(long, decimal, long, decimal)>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 4) continue;
                if (!long.TryParse(parts[0], out var t1)) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var p1)) continue;
                if (!long.TryParse(parts[2], out var t2)) continue;
                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var p2)) continue;
                result.Add((t1, p1, t2, p2));
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty; the next Append rebuilds it cleanly.
        }
        return result;
    }

    public static void Append(string symbol, string contextTag, long t1, decimal p1, long t2, decimal p2)
    {
        var path = PathFor(symbol, contextTag);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var isNew = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (isNew) writer.WriteLine(Header);
        writer.WriteLine($"{t1},{p1.ToString(CultureInfo.InvariantCulture)},{t2},{p2.ToString(CultureInfo.InvariantCulture)}");
    }

    // Removes the first stored rect whose 4 values exactly match (identifies one since it has no
    // separate id) — used when the user deletes it via the Delete key so it doesn't come back.
    public static void Remove(string symbol, string contextTag, long t1, decimal p1, long t2, decimal p2)
    {
        var path = PathFor(symbol, contextTag);
        var existing = Load(symbol, contextTag);
        var idx = existing.FindIndex(r => r.T1 == t1 && r.P1 == p1 && r.T2 == t2 && r.P2 == p2);
        if (idx == -1) return;
        existing.RemoveAt(idx);

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(Header);
        foreach (var r in existing)
            writer.WriteLine($"{r.T1},{r.P1.ToString(CultureInfo.InvariantCulture)},{r.T2},{r.P2.ToString(CultureInfo.InvariantCulture)}");
    }
}
