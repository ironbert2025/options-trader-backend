using System.Text.Json;

namespace OptionsTrader.WinForms;

public record PersistedTrade(
    int      TradeId,
    string   Symbol,
    string   OptionType,
    string   StrikePrice,
    decimal  EntryPrice,
    string   Contracts,
    DateTime EntryTime,
    DateOnly ExpirationDate,
    string   Level,
    string   PnlTarget,
    decimal  EntrySpotPrice = 0m);

// One instance runs per ticker (SPY, QQQ, DIA, ...) as a SEPARATE PROCESS, and they all share this
// one file — Add/Remove used to be a plain Load-then-Save with no cross-process locking, so two
// instances saving within the same few milliseconds could clobber each other: instance B loads a
// snapshot from before instance A's write, then saves that stale snapshot back, silently erasing
// A's trade. Every mutation now holds the file with FileShare.None for its whole read-modify-write
// so the other instance's attempt blocks (and retries) instead of racing.
public static class OpenTradesStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "open_trades.json");

    public static List<PersistedTrade> Load()
    {
        return WithRetry(() =>
        {
            if (!File.Exists(FilePath)) return new List<PersistedTrade>();
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(json)
                ? new List<PersistedTrade>()
                : (JsonSerializer.Deserialize<List<PersistedTrade>>(json) ?? new List<PersistedTrade>());
        }, fallback: new List<PersistedTrade>());
    }

    public static void Add(PersistedTrade trade) => Mutate(trades =>
    {
        trades.RemoveAll(t => t.TradeId == trade.TradeId);
        trades.Add(trade);
    });

    public static void Remove(int tradeId) => Mutate(trades => trades.RemoveAll(t => t.TradeId == tradeId));

    // Holds the file exclusively (FileShare.None) for the entire read-modify-write so no other
    // instance's Add/Remove/Load can interleave and clobber this one's change.
    private static void Mutate(Action<List<PersistedTrade>> mutate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        WithRetry<object?>(() =>
        {
            using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            List<PersistedTrade> trades;
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                var json = reader.ReadToEnd();
                trades = string.IsNullOrWhiteSpace(json)
                    ? new List<PersistedTrade>()
                    : (JsonSerializer.Deserialize<List<PersistedTrade>>(json) ?? new List<PersistedTrade>());
            }

            mutate(trades);

            stream.SetLength(0);
            stream.Position = 0;
            using (var writer = new StreamWriter(stream, leaveOpen: true))
                writer.Write(JsonSerializer.Serialize(trades));

            return null;
        }, fallback: null);
    }

    // Another instance can briefly hold the exclusive lock (Mutate) while this one is trying to
    // Load or Mutate too — retry a few times with a short delay instead of giving up immediately.
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
