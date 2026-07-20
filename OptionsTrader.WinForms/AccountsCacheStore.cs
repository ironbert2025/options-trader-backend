using System.Text.Json;
using OptionsTrader.Application.DTOs.Trading;

namespace OptionsTrader.WinForms;

internal static class AccountsCacheStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "accounts_cache.json");

    public static List<BrokerAccountDto> Load()
    {
        if (!File.Exists(SettingsPath)) return new List<BrokerAccountDto>();
        try
        {
            return JsonSerializer.Deserialize<List<BrokerAccountDto>>(File.ReadAllText(SettingsPath))
                   ?? new List<BrokerAccountDto>();
        }
        catch
        {
            return new List<BrokerAccountDto>();
        }
    }

    public static void Save(List<BrokerAccountDto> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(accounts));
    }
}
