using System.Text.Json;

namespace OptionsTrader.WinForms;

// Full local history of every trade (demo and real, open AND closed — never removed, unlike
// OpenTradesStore which only tracks currently-open trades and deletes them on close). This is the
// first step toward being able to run without the EC2 API backend: today it's a dual-write
// alongside SaveTradeToApiAsync/CloseTradeInApiAsync (the API stays the source of truth for now),
// but if the API is unreachable when opening a trade, a local negative id is assigned instead of
// losing the trade entirely.
public record TradeRecord(
    int Id,
    string Symbol,
    string OptionType,
    decimal StrikePrice,
    decimal SpotPrice,
    DateOnly ExpirationDate,
    decimal EntryPrice,
    decimal? ExitPrice,
    DateTime EntryTime,
    int Contracts,
    int Level,
    decimal TargetPercent,
    TimeSpan? Duration,
    decimal? Pnl,
    decimal? PnlPercent,
    bool IsDemo,
    string Broker);

// One instance runs per ticker as a SEPARATE OS process, all appending/updating this SAME file —
// same exclusive-lock-with-retry pattern as OpenTradesStore/TelegramPushStore.
internal static class TradeHistoryStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "trade_history.json");

    public static List<TradeRecord> Load()
    {
        return WithRetry(() =>
        {
            if (!File.Exists(FilePath)) return new List<TradeRecord>();
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(json)
                ? new List<TradeRecord>()
                : (JsonSerializer.Deserialize<List<TradeRecord>>(json) ?? new List<TradeRecord>());
        }, fallback: new List<TradeRecord>());
    }

    // Appends a new trade. If record.Id is 0 (the API call failed and returned no real id), a
    // local id is assigned instead — the next negative integer below whatever's already in use
    // (-1, -2, -3, ...) so it can never collide with a real (positive) API-assigned id. Returns
    // the id actually stored (== record.Id when it was already nonzero).
    public static int Add(TradeRecord record)
    {
        var assignedId = record.Id;

        Mutate(trades =>
        {
            if (assignedId == 0)
            {
                var lowestLocalId = trades.Where(t => t.Id < 0).Select(t => t.Id).DefaultIfEmpty(0).Min();
                assignedId = lowestLocalId - 1;
            }

            trades.RemoveAll(t => t.Id == assignedId);
            trades.Add(record with { Id = assignedId });
        });

        return assignedId;
    }

    // Updates an existing trade's close fields in place — mirrors CloseTradeInApiAsync's payload.
    // No-op if the id isn't found (e.g. Add's API call itself failed AND this instance never
    // fell back to persisting locally either — best-effort, same as every other store here).
    public static void Close(int id, decimal exitPrice, decimal pnl, decimal pnlPercent, TimeSpan duration)
    {
        Mutate(trades =>
        {
            var idx = trades.FindIndex(t => t.Id == id);
            if (idx < 0) return;
            trades[idx] = trades[idx] with { ExitPrice = exitPrice, Pnl = pnl, PnlPercent = pnlPercent, Duration = duration };
        });
    }

    private static void Mutate(Action<List<TradeRecord>> mutate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        WithRetry<object?>(() =>
        {
            using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            List<TradeRecord> trades;
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                var json = reader.ReadToEnd();
                trades = string.IsNullOrWhiteSpace(json)
                    ? new List<TradeRecord>()
                    : (JsonSerializer.Deserialize<List<TradeRecord>>(json) ?? new List<TradeRecord>());
            }

            mutate(trades);

            stream.SetLength(0);
            stream.Position = 0;
            using (var writer = new StreamWriter(stream, leaveOpen: true))
                writer.Write(JsonSerializer.Serialize(trades));

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
