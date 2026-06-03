using System.Text.Json;

namespace OptionsTrader.WinForms;

public record SchwabCredentials(string ApiKey, string ApiSecret);

public static class SchwabCredentialsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "schwab_credentials.json");

    public static SchwabCredentials Load()
    {
        if (!File.Exists(FilePath)) return new SchwabCredentials(string.Empty, string.Empty);
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<SchwabCredentials>(json) ?? new SchwabCredentials(string.Empty, string.Empty);
    }

    public static void Save(SchwabCredentials credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(credentials));
    }
}
