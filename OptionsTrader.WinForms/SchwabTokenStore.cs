using System.Text.Json;

namespace OptionsTrader.WinForms;

public record SchwabTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);

public static class SchwabTokenStore
{
    private const string FileName = "schwab_tokens.json";

    private static readonly string LocalFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", FileName);

    // Resolved fresh on every Load/Save — points at a shared network folder when
    // TokenShareSettingsStore has one configured (multi-PC token sharing: only the hub instance
    // writes there, everyone else just reads), otherwise falls back to the local AppData path.
    private static string ResolveFilePath()
    {
        var shared = TokenShareSettingsStore.Load();
        return string.IsNullOrWhiteSpace(shared) ? LocalFilePath : Path.Combine(shared, FileName);
    }

    public static SchwabTokens? Load()
    {
        var path = ResolveFilePath();
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<SchwabTokens>(stream);
    }

    public static void Save(SchwabTokens tokens)
    {
        var path = ResolveFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, tokens);
    }
}
