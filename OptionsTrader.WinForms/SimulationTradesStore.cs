using System.Text.Json;

namespace OptionsTrader.WinForms;

// "Trade Simulation" trades (Charts tab, OpenSimulationTradeFromRow) never get a real tradeId —
// RecordEntryAsync always leaves it at 0 for these, on purpose, so they skip TradeHistoryStore/the
// API/screenshots entirely (see its own comment). That 0 is shared by every simulation trade, so
// it can't double as the unique key OpenTradesStore uses (TradeId) — each persisted row here
// carries its own LocalId (Guid, generated in RecordEntryAsync and stashed on the row's
// TradeRowTag) instead, used to Add/Remove/match it later.
public record PersistedSimulationTrade(
    Guid     LocalId,
    string   Symbol,
    string   OptionType,
    string   StrikePrice,
    decimal  EntryPrice,
    string   Contracts,
    DateTime EntryTime,
    DateOnly ExpirationDate,
    string   Level,
    string   PnlTarget,
    decimal  EntrySpotPrice = 0m,
    string   EntrySpotColor = "#ffffff");

// Same cross-process file-locking shape as OpenTradesStore — see that file's own comment for why
// a plain Load-then-Save isn't safe here (multiple ticker instances share this one file).
public static class SimulationTradesStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "open_simulation_trades.json");

    public static List<PersistedSimulationTrade> Load()
    {
        return WithRetry(() =>
        {
            if (!File.Exists(FilePath)) return new List<PersistedSimulationTrade>();
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(json)
                ? new List<PersistedSimulationTrade>()
                : (JsonSerializer.Deserialize<List<PersistedSimulationTrade>>(json) ?? new List<PersistedSimulationTrade>());
        }, fallback: new List<PersistedSimulationTrade>());
    }

    public static void Add(PersistedSimulationTrade trade) => Mutate(trades =>
    {
        trades.RemoveAll(t => t.LocalId == trade.LocalId);
        trades.Add(trade);
    });

    public static void Remove(Guid localId) => Mutate(trades => trades.RemoveAll(t => t.LocalId == localId));

    private static void Mutate(Action<List<PersistedSimulationTrade>> mutate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        WithRetry<object?>(() =>
        {
            using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            List<PersistedSimulationTrade> trades;
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                var json = reader.ReadToEnd();
                trades = string.IsNullOrWhiteSpace(json)
                    ? new List<PersistedSimulationTrade>()
                    : (JsonSerializer.Deserialize<List<PersistedSimulationTrade>>(json) ?? new List<PersistedSimulationTrade>());
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
                return fallback;
            }
        }
        return fallback;
    }
}
