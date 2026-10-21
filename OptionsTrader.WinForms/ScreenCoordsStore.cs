using System.Text.Json;

namespace OptionsTrader.WinForms;

public record TickerCoords(string Coords1, string Coords2);

// Keyed by ticker symbol so any symbol added to the Tickers table automatically gets its own
// screen-capture coordinates — no longer limited to a fixed set of 4 hardcoded symbols.
public static class ScreenCoordsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "screen_coords.json");

    public static Dictionary<string, TickerCoords> Load()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, TickerCoords>();
        try
        {
            var json = File.ReadAllText(FilePath);
            // The old fixed-record format ({"AAPL":{...},"TSLA":{...},"SPY":{...},"QQQ":{...}})
            // serializes identically to a Dictionary<string, TickerCoords> — no migration needed,
            // it deserializes straight into the new shape.
            return JsonSerializer.Deserialize<Dictionary<string, TickerCoords>>(json)
                   ?? new Dictionary<string, TickerCoords>();
        }
        catch
        {
            return new Dictionary<string, TickerCoords>();
        }
    }

    public static void Save(Dictionary<string, TickerCoords> coords)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(coords));
    }
}
