using System.Text.Json;

namespace OptionsTrader.WinForms;

// Whether to show the "Confirm REAL Order" dialog before sending a real market order (Trade or
// Trade-Target). Defaults to false (not shown) — a fresh install sends real orders straight
// through unless the user explicitly opts in to the confirmation dialog in Settings.
internal static class OrderConfirmationSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "order_confirmation_settings.json");

    public static bool Load()
    {
        if (!File.Exists(SettingsPath))
            return false;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ShowOrderConfirmation", out var val) && val.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public static void Save(bool value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(new { ShowOrderConfirmation = value });
        File.WriteAllText(SettingsPath, json);
    }
}
