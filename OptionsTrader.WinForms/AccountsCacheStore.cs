using System.Text.Json;
using OptionsTrader.Application.DTOs.Trading;

namespace OptionsTrader.WinForms;

internal static class AccountsCacheStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "accounts_cache.json");

    public static List<SchwabAccountDto> Load()
    {
        if (!File.Exists(SettingsPath)) return new List<SchwabAccountDto>();
        try
        {
            return JsonSerializer.Deserialize<List<SchwabAccountDto>>(File.ReadAllText(SettingsPath))
                   ?? new List<SchwabAccountDto>();
        }
        catch
        {
            return new List<SchwabAccountDto>();
        }
    }

    public static void Save(List<SchwabAccountDto> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(accounts));
    }
}
