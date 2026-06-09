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
    string   PnlTarget);

public static class OpenTradesStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "open_trades.json");

    public static List<PersistedTrade> Load()
    {
        if (!File.Exists(FilePath)) return new List<PersistedTrade>();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PersistedTrade>>(json) ?? new List<PersistedTrade>();
        }
        catch { return new List<PersistedTrade>(); }
    }

    public static void Add(PersistedTrade trade)
    {
        var trades = Load();
        trades.RemoveAll(t => t.TradeId == trade.TradeId);
        trades.Add(trade);
        Save(trades);
    }

    public static void Remove(int tradeId)
    {
        var trades = Load();
        trades.RemoveAll(t => t.TradeId == tradeId);
        Save(trades);
    }

    private static void Save(List<PersistedTrade> trades)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(trades));
    }
}
