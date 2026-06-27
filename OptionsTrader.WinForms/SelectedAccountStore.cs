using System.Text.Json;

namespace OptionsTrader.WinForms;

public record SelectedAccount(string AccountNumber, string HashValue);

internal static class SelectedAccountStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "selected_account.json");

    public static SelectedAccount? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<SelectedAccount>(File.ReadAllText(SettingsPath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SelectedAccount account)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(account));
    }
}
