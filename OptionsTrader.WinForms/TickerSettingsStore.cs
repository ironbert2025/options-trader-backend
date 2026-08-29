using System.Text.Json;

namespace OptionsTrader.WinForms;

internal static class TickerSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "tickers.json");

    public static List<TickerEntry> Load()
    {
        if (!File.Exists(SettingsPath))
            return [];

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<List<TickerEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<TickerEntry> tickers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(tickers, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}

internal record TickerEntry(string Symbol, string Low, string High, string ExpDate, bool TelegramEnabled = true, bool AwsEnabled = true, int PollingIntervalSeconds = 6);
