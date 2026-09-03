using System.Text.Json;

namespace OptionsTrader.WinForms;

// Persists the IP of a REMOTE hub instance (on another computer on the LAN) to connect to for
// live candle data, same pattern as SchwabCredentialsStore/TelegramSettingsStore —
// %AppData%\OptionsTrader\hubhost.json. Empty/unset (the default) means "no remote hub configured
// — this instance decides for itself whether to become the hub or connect to one on this same
// machine", same behavior as before this setting existed.
internal static class HubHostSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "hubhost.json");

    private sealed class Data
    {
        public string RemoteHubHost { get; set; } = "";
    }

    public static string Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
            return data.RemoteHubHost;
        }
        catch
        {
            return "";
        }
    }

    public static void Save(string remoteHubHost)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { RemoteHubHost = remoteHubHost }));
    }
}
