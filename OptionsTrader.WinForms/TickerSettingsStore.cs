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
// StrikeCount: how many strikes PER SIDE (calls AND puts each) get fetched from Schwab's chains
// endpoint — was a single hardcoded 40 for every symbol regardless of how much that symbol
// actually moves in a day, per explicit request now tunable per symbol. Default 40 keeps existing
// tickers' behavior unchanged until explicitly set.
internal record TickerEntry(string Symbol, string Low, string High, string ExpDate, bool TelegramEnabled = true, bool AwsEnabled = true, int PollingIntervalSeconds = 6, bool DailyPmLineEnabled = true, List<int>? DailySmaLinesEnabled = null, int StrikeCount = 40);
