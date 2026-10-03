using System.Drawing;
using System.Text.Json;

namespace OptionsTrader.WinForms;

// Per-ticker main-window screen position ("Guardar Posición" button, Settings tab) — so each
// instance reopens where it was left, keyed by the ticker symbol it's running. Manual ticker
// switches during a session never read this (see Form1.TickerButton_Click) — only applied once,
// right after the instance auto-selects its own ticker at startup.
internal static class WindowPositionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "window_positions.json");

    private static Dictionary<string, Point> LoadAll()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var json = File.ReadAllText(FilePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, int[]>>(json) ?? new();
            return raw.ToDictionary(kv => kv.Key, kv => new Point(kv.Value[0], kv.Value[1]));
        }
        catch
        {
            return new(); // corrupt/partial file — never let this block startup
        }
    }

    public static Point? Load(string symbol)
    {
        var all = LoadAll();
        return all.TryGetValue(symbol, out var p) ? p : null;
    }

    public static void Save(string symbol, Point location)
    {
        var all = LoadAll();
        all[symbol] = location;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var raw = all.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.X, kv.Value.Y });
        File.WriteAllText(FilePath, JsonSerializer.Serialize(raw));
    }
}
