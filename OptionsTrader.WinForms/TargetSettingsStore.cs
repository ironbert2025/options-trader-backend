using System.Text.Json;

namespace OptionsTrader.WinForms;

internal static class TargetSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "target.json");

    public static string Load()
    {
        if (!File.Exists(SettingsPath))
            return string.Empty;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SelectedTarget", out var val)
                ? val.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Save(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(new { SelectedTarget = value });
        File.WriteAllText(SettingsPath, json);
    }
}
