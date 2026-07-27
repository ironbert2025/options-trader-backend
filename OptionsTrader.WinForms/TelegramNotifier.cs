using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OptionsTrader.WinForms;

// Sends push messages/photos to a Telegram channel via the Bot API (sendMessage/sendPhoto).
// Ported from the TradeSignal project, where this exact code is already proven in production.
public static class TelegramNotifier
{
    private static readonly HttpClient Http = new();
    private const string LogFolder = @"C:\OptionsTraderPush";

    public static async Task<(bool Ok, string Detail, long? MessageId)> SendAsync(string botToken, string chatId, string text, string symbol)
    {
        SaveLog(symbol, text);

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Bot Token o Chat ID vacío", null);

        string url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        string payload = JsonSerializer.Serialize(new { chat_id = chatId, text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            var resp = await Http.PostAsync(url, content);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, body, null);

            using var doc = JsonDocument.Parse(body);
            long? messageId = doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("message_id", out var idProp)
                ? idProp.GetInt64()
                : null;
            return (true, "OK", messageId);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    // Sends an image (sendPhoto).
    public static async Task<(bool Ok, string Detail, long? MessageId)> SendPhotoAsync(string botToken, string chatId, string filePath, string? caption = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Bot Token o Chat ID vacío", null);

        string url = $"https://api.telegram.org/bot{botToken}/sendPhoto";
        using var content = new MultipartFormDataContent
        {
            { new StringContent(chatId), "chat_id" }
        };
        if (!string.IsNullOrEmpty(caption))
            content.Add(new StringContent(caption), "caption");

        using var fs = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "photo", Path.GetFileName(filePath));

        try
        {
            var resp = await Http.PostAsync(url, content);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, body, null);

            using var doc = JsonDocument.Parse(body);
            long? messageId = doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("message_id", out var idProp)
                ? idProp.GetInt64()
                : null;
            return (true, "OK", messageId);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task<(bool Ok, string Detail)> DeleteMessageAsync(string botToken, string chatId, long messageId)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Bot Token o Chat ID vacío");

        string url = $"https://api.telegram.org/bot{botToken}/deleteMessage";
        string payload = JsonSerializer.Serialize(new { chat_id = chatId, message_id = messageId });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            var resp = await Http.PostAsync(url, content);
            string body = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode ? (true, "OK") : (false, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // Saves a local copy of every sent text message — symbol_yyyyMMdd_HHmm.txt.
    private static void SaveLog(string symbol, string text)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            string fileName = $"{symbol}_{DateTime.Now:yyyyMMdd_HHmm}.txt";
            File.WriteAllText(Path.Combine(LogFolder, fileName), text);
        }
        catch
        {
            // Don't block the send if the local log fails.
        }
    }
}
