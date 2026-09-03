using System.Text.Json;

namespace OptionsTrader.WinForms;

// Filled in by hand for now (%AppData%\OptionsTrader\earnings.json) — no UI to edit it yet, just
// a lookup so Form1 can show the next earnings date for whichever ticker is selected.
internal static class EarningsDateStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "earnings.json");

    public static List<EarningsEntry> Load()
    {
        if (!File.Exists(SettingsPath))
            return [];

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<List<EarningsEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

internal record EarningsEntry(string Symbol, DateOnly EarningDate);
