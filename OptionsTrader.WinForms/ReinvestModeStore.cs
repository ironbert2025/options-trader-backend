using System.Text.Json;

namespace OptionsTrader.WinForms;

// Where the net result (PnL minus commission) of a closed Demo / Simulation trade goes:
//  Balance - into the demo balance, so the position size (a % of it) grows or shrinks with it;
//  Premium - into a separate reinvestment fund that is ADDED to the position amount (balance x % + fund),
//            leaving the balance untouched. Gains build the fund; losses and commissions eat it, never below 0.
public enum ReinvestMode { Balance, Premium }

internal static class ReinvestModeStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptionsTrader", "reinvest_mode.json");

    public static ReinvestMode Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ReinvestMode.Balance;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("Mode", out var v) && v.GetString() == nameof(ReinvestMode.Premium)
                ? ReinvestMode.Premium : ReinvestMode.Balance;
        }
        catch { return ReinvestMode.Balance; }
    }

    public static void Save(ReinvestMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { Mode = mode.ToString() }));
    }
}
