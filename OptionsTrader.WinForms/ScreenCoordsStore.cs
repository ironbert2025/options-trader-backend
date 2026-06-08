using System.Text.Json;

namespace OptionsTrader.WinForms;

public record TickerCoords(string Coords1, string Coords2);

public record ScreenCoords(
    TickerCoords AAPL,
    TickerCoords TSLA,
    TickerCoords SPY,
    TickerCoords QQQ);

public static class ScreenCoordsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "screen_coords.json");

    public static ScreenCoords Load()
    {
        if (!File.Exists(FilePath)) return Empty();
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<ScreenCoords>(json) ?? Empty();
    }

    public static void Save(ScreenCoords coords)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(coords));
    }

    private static ScreenCoords Empty() => new(
        new TickerCoords(string.Empty, string.Empty),
        new TickerCoords(string.Empty, string.Empty),
        new TickerCoords(string.Empty, string.Empty),
        new TickerCoords(string.Empty, string.Empty));
}
