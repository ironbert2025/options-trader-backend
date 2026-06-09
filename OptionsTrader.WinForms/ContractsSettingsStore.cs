using System.Text.Json;

namespace OptionsTrader.WinForms;

public static class ContractsSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "contracts_settings.json");

    public static string Load()
    {
        if (!File.Exists(FilePath)) return "1";
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<string>(json) ?? "1";
    }

    public static void Save(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(value));
    }
}
