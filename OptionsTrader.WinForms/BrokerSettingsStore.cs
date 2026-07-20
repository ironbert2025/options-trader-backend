using System.Text.Json;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.WinForms;

internal static class BrokerSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "settings.json");

    // Defaults to Schwab (the only implemented broker) when nothing was ever saved or the
    // saved value doesn't match a known BrokerName.
    public static BrokerName Load()
    {
        if (!File.Exists(SettingsPath))
            return BrokerName.Schwab;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SelectedBroker", out var val)
                && Enum.TryParse<BrokerName>(val.GetString(), out var broker))
            {
                return broker;
            }
            return BrokerName.Schwab;
        }
        catch
        {
            return BrokerName.Schwab;
        }
    }

    public static void Save(BrokerName broker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(new { SelectedBroker = broker.ToString() });
        File.WriteAllText(SettingsPath, json);
    }
}
