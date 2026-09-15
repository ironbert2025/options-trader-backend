namespace OptionsTrader.WinForms;

// Appends one markdown entry per closed trade to a daily Obsidian note — one file PER TRADE TYPE
// per day per PC, per explicit request (previously Real/Demo were mixed into one "_Trades.md"):
// Demo -> "_Trades.md" (unchanged filename, for compatibility with everything already written),
// Real -> "_Real_Trades.md", Simulation -> "_Sim_Trades.md" (see AppendSimTrade — Simulation
// trades never reach AppendTrade at all, they skip TradeHistoryStore/S3 entirely). Multiple
// machines can run instances (see the Hub Host / Token Share multi-PC setup), so the PC name in
// the filename keeps each machine's trades in its own file instead of racing to write the same
// one. Creates the folder tree if it doesn't exist.
internal static class DailyTradeLogWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    public static void AppendTrade(TradeRecord trade)
    {
        var suffix = trade.IsDemo ? "Trades" : "Real_Trades";
        var time = trade.EntryTime.ToString("HH:mm:ss");
        var nl   = Environment.NewLine;
        // Refuerzo note — see Form1.TryCreateReinforcementAsync/TradeHistoryStore.MarkReinforcement.
        var reinforcementNote = trade.IsReinforcement
            ? $"**Refuerzo**: resultado de combinar los trades #{trade.SourceTradeIds}{nl}{nl}"
            : string.Empty;
        var entry =
            $"### {trade.Symbol} ({trade.OptionType}, {time}){nl}{nl}" +
            reinforcementNote +
            $"**Open**{nl}![Open]({trade.EntryImageUrl}){nl}{nl}" +
            $"**Close**{nl}![Close]({trade.CloseImageUrl}){nl}{nl}" +
            $"**TradeLog**{nl}![TradeLog]({trade.TradeLogImageUrl}){nl}{nl}" +
            $"---{nl}{nl}";

        Append(suffix, entry);
    }

    // Simulation trades (Charts tab "Trade Simulation") never reach AppendTrade above — they skip
    // TradeHistoryStore/S3 entirely (see Form1.RecordEntryAsync/CloseTradeRowAsync's isSimulation
    // gates), so there's no TradeRecord/S3 URLs to work with. Images are the same local PNGs
    // already saved for the entry/close snapshots — embedded via file:// like EventLogMarkdownWriter
    // does, instead of an S3 URL.
    public static void AppendSimTrade(string symbol, string optionType, DateTime entryTime,
        string? entryImagePath, string? closeImagePath)
    {
        var time = entryTime.ToString("HH:mm:ss");
        var nl   = Environment.NewLine;
        var openLine  = entryImagePath != null ? $"**Open**{nl}![Open]({new Uri(entryImagePath).AbsoluteUri}){nl}{nl}" : string.Empty;
        var closeLine = closeImagePath != null ? $"**Close**{nl}![Close]({new Uri(closeImagePath).AbsoluteUri}){nl}{nl}" : string.Empty;
        var entry =
            $"### {symbol} ({optionType}, {time}){nl}{nl}" +
            openLine +
            closeLine +
            $"---{nl}{nl}";

        Append("Sim_Trades", entry);
    }

    private static void Append(string suffix, string entry)
    {
        try
        {
            var dateStr = DateTime.Now.ToString("yyyy_MM_dd");
            var dayFolder = Path.Combine(VaultFolder, dateStr);
            Directory.CreateDirectory(dayFolder);
            var fileName = $"{dateStr}_{Environment.MachineName}_{suffix}.md";
            var path = Path.Combine(dayFolder, fileName);

            WithRetry(() =>
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(entry);
            });
        }
        catch
        {
            // Best-effort, same as every other local store here — never let this affect the
            // trade-close flow that just finished.
        }
    }

    private static void WithRetry(Action action)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return;
            }
        }
    }
}
