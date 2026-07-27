using System.Text.Json;

namespace OptionsTrader.WinForms;

// Persists Bot Token and Chat ID for Telegram push notifications, same pattern as
// SchwabCredentialsStore/AwsSettingsStore — %AppData%\OptionsTrader\telegram.json.
public static class TelegramSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "telegram.json");

    private sealed class Data
    {
        public string BotToken { get; set; } = "";
        public string ChatId { get; set; } = "";
    }

    public static (string BotToken, string ChatId) Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return ("", "");
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
            return (data.BotToken, data.ChatId);
        }
        catch
        {
            return ("", "");
        }
    }

    public static void Save(string botToken, string chatId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { BotToken = botToken, ChatId = chatId }));
    }
}
