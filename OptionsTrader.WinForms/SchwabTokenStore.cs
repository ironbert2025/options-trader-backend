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
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<SchwabTokens>(json);
    }

    public static void Save(SchwabTokens tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(tokens));
    }
}
