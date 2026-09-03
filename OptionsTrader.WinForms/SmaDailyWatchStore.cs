using System.Globalization;

namespace OptionsTrader.WinForms;

// Persists which Daily-timeframe SMA periods (20/40/100/200) are currently being watched for a
// live-price cross, per symbol — armed from DailyChartForm's "SMA Watch" buttons, monitored by
// the live 1h panel (ChartPanel, Hourly15 mode) on every tick — see
// ChartPanel.EvaluateSmaCrossWatches. Survives closing/reopening either window; only removed when
// the user explicitly deletes it (Delete key on the chart marker, or clicking the toolbar button
// again), never auto-cleared just because it fired.
internal static class SmaDailyWatchStore
{
    private const string OutputFolder = @"C:\OptionsData\ChartDrawings";
    private const string Header = "Period";

    private static string PathFor(string symbol) => Path.Combine(OutputFolder, symbol, $"{symbol}_SmaWatches.csv");

    public static List<int> Load(string symbol)
    {
        var path = PathFor(symbol);
        var result = new List<int>();
        if (!File.Exists(path)) return result;

        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (int.TryParse(lines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var period))
                    result.Add(period);
            }
        }
        catch
        {
            // Corrupt/partial file — treat as empty.
        }
        return result;
    }

    public static void Add(string symbol, int period)
    {
        var existing = Load(symbol);
        if (existing.Contains(period)) return;
        existing.Add(period);
        Save(symbol, existing);
    }

    public static void Remove(string symbol, int period)
    {
        var existing = Load(symbol);
        if (!existing.Remove(period)) return;
        Save(symbol, existing);
    }

    private static void Save(string symbol, List<int> periods)
    {
        var path = PathFor(symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(Header);
        foreach (var p in periods)
            writer.WriteLine(p.ToString(CultureInfo.InvariantCulture));
    }
}
