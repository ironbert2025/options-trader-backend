using System.Text.Json;

namespace OptionsTrader.WinForms;

public record TelegramPush(long MessageId, string ChatId, string Symbol, string Kind, DateTime SentAt);

// Every Telegram push this app has ever sent (across ALL ticker instances — they all push to the
// SAME channel), so a single "delete everything" button in any instance's Form1 can clean up
// pushes sent by every other instance too. One instance runs per ticker as a SEPARATE OS process,
// so this uses the same exclusive-lock-with-retry pattern as OpenTradesStore/EventLogStore.
internal static class TelegramPushStore
{
    private static readonly string FilePath = Path.Combine(
        @"C:\OptionsTraderPush", "telegram_pushes.json");

    public static List<TelegramPush> Load()
    {
        return WithRetry(() =>
        {
            if (!File.Exists(FilePath)) return new List<TelegramPush>();
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(json)
                ? new List<TelegramPush>()
                : (JsonSerializer.Deserialize<List<TelegramPush>>(json) ?? new List<TelegramPush>());
        }, fallback: new List<TelegramPush>());
    }

    public static void Append(TelegramPush push) => Mutate(pushes => pushes.Add(push));

    // Called once the "delete all" sweep finishes — no point keeping entries around once
    // Telegram either deleted them or rejected the delete (too old to retry either way).
    public static void Clear() => Mutate(pushes => pushes.Clear());

    private static void Mutate(Action<List<TelegramPush>> mutate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        WithRetry<object?>(() =>
        {
            using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            List<TelegramPush> pushes;
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                var json = reader.ReadToEnd();
                pushes = string.IsNullOrWhiteSpace(json)
                    ? new List<TelegramPush>()
                    : (JsonSerializer.Deserialize<List<TelegramPush>>(json) ?? new List<TelegramPush>());
            }

            mutate(pushes);

            stream.SetLength(0);
            stream.Position = 0;
            using (var writer = new StreamWriter(stream, leaveOpen: true))
                writer.Write(JsonSerializer.Serialize(pushes));

            return null;
        }, fallback: null);
    }

    private static T WithRetry<T>(Func<T> action, T fallback)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return fallback; // corrupt file or similar — not a locking issue, don't retry
            }
        }
        return fallback;
    }
}
