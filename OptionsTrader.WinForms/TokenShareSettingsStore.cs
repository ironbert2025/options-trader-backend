using System.Text.Json;

namespace OptionsTrader.WinForms;

// Persists an optional shared network folder for schwab_tokens.json so multiple PCs can read the
// same token file — only the hub instance (see SchwabAuthService's allowRefresh gate) ever writes
// to it, everyone else (other local instances AND other PCs pointed at the same folder) only
// reads. Empty/unset (the default) keeps SchwabTokenStore on the local %AppData% path, same as
// before this setting existed — single-machine setups need no configuration at all.
internal static class TokenShareSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "tokenshare.json");

    private sealed class Data
    {
        public string SharedFolder { get; set; } = "";
    }

    public static string Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
            return data.SharedFolder;
        }
        catch
        {
            return "";
        }
    }

    public static void Save(string sharedFolder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { SharedFolder = sharedFolder }));
    }
}
