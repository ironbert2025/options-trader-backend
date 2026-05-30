using System.Text.Json;

namespace OptionsTrader.WinForms;

internal static class PositionSizeSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "positionsize.json");

    public static string Load()
    {
        if (!File.Exists(SettingsPath))
            return string.Empty;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SelectedPositionSize", out var val)
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
        var json = JsonSerializer.Serialize(new { SelectedPositionSize = value });
        File.WriteAllText(SettingsPath, json);
    }
}
