using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists T-Line drawings on the 1h chart panel, per symbol, so they reappear at the same point
// after closing and reopening the app. Times are the same "ET digits disguised as UTC" epoch
// seconds chart.html already works in (see ChartPanel.ToChartJson) — stored and round-tripped
// as opaque longs here, never interpreted as a real UTC instant on the C# side.
internal static class TLineStore
{
    private const string OutputFolder = @"C:\OptionsData";
    private const string Header = "Time1,Price1,Time2,Price2";

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, $"{symbol}_TLines.csv");

    public static List<(long T1, decimal P1, long T2, decimal P2)> Load(string symbol)
    {
        var path = PathFor(symbol);
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

    public static void Append(string symbol, long t1, decimal p1, long t2, decimal p2)
    {
        Directory.CreateDirectory(OutputFolder);
        var path = PathFor(symbol);
        var isNew = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (isNew) writer.WriteLine(Header);
        writer.WriteLine($"{t1},{p1.ToString(CultureInfo.InvariantCulture)},{t2},{p2.ToString(CultureInfo.InvariantCulture)}");
    }

    public static void Clear(string symbol)
    {
        var path = PathFor(symbol);
        if (File.Exists(path)) File.Delete(path);
    }
}
