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

// DailySmaLinesEnabled: which Daily SMA periods (40/100/200) get the solid tab-Charts-only
// reference line — null/empty means none (D.PM/period 20 has its own separate DailyPmLineEnabled
// above and is unaffected by this list).
internal record TickerEntry(string Symbol, string Low, string High, string ExpDate, bool TelegramEnabled = true, bool AwsEnabled = true, int PollingIntervalSeconds = 6, bool DailyPmLineEnabled = true, List<int>? DailySmaLinesEnabled = null);
