using System.Text.Json;

namespace OptionsTrader.WinForms;

public record SchwabTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);

public static class SchwabTokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "schwab_tokens.json");

    public static SchwabTokens? Load()
    {
        if (!File.Exists(FilePath)) return null;
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<SchwabTokens>(stream);
    }

    public static void Save(SchwabTokens tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, tokens);
    }
}
