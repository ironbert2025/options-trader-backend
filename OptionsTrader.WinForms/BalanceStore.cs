using System.Text.Json;

namespace OptionsTrader.WinForms;

public static class BalanceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "balance.json");

    public static decimal Load()
    {
        if (!File.Exists(FilePath)) return 0m;
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<decimal>(json);
    }

    public static void Save(decimal balance)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(balance));
    }
}
