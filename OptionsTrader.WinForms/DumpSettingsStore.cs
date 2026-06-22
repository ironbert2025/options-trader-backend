using System.Text.Json;

namespace OptionsTrader.WinForms;

internal static class DumpSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "dumpsettings.json");

    public static bool Load()
    {
        if (!File.Exists(SettingsPath))
            return false;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SaveDumps", out var val) && val.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public static void Save(bool value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(new { SaveDumps = value });
        File.WriteAllText(SettingsPath, json);
    }
}
