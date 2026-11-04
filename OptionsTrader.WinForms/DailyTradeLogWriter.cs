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

    // An entry's image, with the external-screen screenshot (when there is one) in a second column to its right: a
    // two-column table, so the row is as tall as the taller image and whatever follows starts below it.
    private static string WithExternal(string leftMarkdown, string? externalPath, string name)
    {
        if (externalPath == null) return leftMarkdown;
        var nl = Environment.NewLine;
        return $"| | |{nl}|---|---|{nl}| {leftMarkdown} | ![{name}]({new Uri(externalPath).AbsoluteUri}) |";
    }

    public static void AppendTrade(TradeRecord trade, string? openExtPath = null, string? closeExtPath = null)
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
            $"**Open**{nl}{WithExternal($"![Open]({trade.EntryImageUrl})", openExtPath, "Open externo")}{nl}{nl}" +
            $"**Close**{nl}{WithExternal($"![Close]({trade.CloseImageUrl})", closeExtPath, "Close externo")}{nl}{nl}" +
            $"**TradeLog**{nl}![TradeLog]({trade.TradeLogImageUrl}){nl}{nl}" +
            $"---{nl}{nl}";

        // Filed under the day the trade OPENED, not the day it closed — a trade opened Monday and
        // closed Tuesday used to land in Tuesday's file (Append used DateTime.Now), splitting its
        // own Open image away from where the rest of that day's activity lives. Per explicit
        // request: the whole entry (Open+Close+TradeLog together) belongs with its entry day.
        Append(suffix, entry, trade.EntryTime.Date);
    }

    // Simulation trades (Charts tab "Trade Simulation") never reach AppendTrade above — they skip
    // TradeHistoryStore/S3 entirely (see Form1.RecordEntryAsync/CloseTradeRowAsync's isSimulation
    // gates), so there's no TradeRecord/S3 URLs to work with. Images are the same local PNGs
    // already saved for the entry/close snapshots — embedded via file:// like EventLogMarkdownWriter
    // does, instead of an S3 URL.
    public static void AppendSimTrade(string symbol, string optionType, DateTime entryTime,
        string? entryImagePath, string? closeImagePath, string? tradeLogImagePath = null,
        string? openExtPath = null, string? closeExtPath = null)
    {
        var time = entryTime.ToString("HH:mm:ss");
        var nl   = Environment.NewLine;
        var openLine     = entryImagePath != null ? $"**Open**{nl}{WithExternal($"![Open]({new Uri(entryImagePath).AbsoluteUri})", openExtPath, "Open externo")}{nl}{nl}" : string.Empty;
        var closeLine    = closeImagePath != null ? $"**Close**{nl}{WithExternal($"![Close]({new Uri(closeImagePath).AbsoluteUri})", closeExtPath, "Close externo")}{nl}{nl}" : string.Empty;
        // Trade Log (Trades + Logger section screenshot) — per explicit request, saved locally
        // same as Real/Demo trades already do (see Form1.CaptureTradeLogScreenshot), just embedded
        // via file:// like everything else here instead of an S3 URL (Simulation never uploads).
        var tradeLogLine = tradeLogImagePath != null ? $"**TradeLog**{nl}![TradeLog]({new Uri(tradeLogImagePath).AbsoluteUri}){nl}{nl}" : string.Empty;
        var entry =
            $"### {symbol} ({optionType}, {time}){nl}{nl}" +
            openLine +
            closeLine +
            tradeLogLine +
            $"---{nl}{nl}";

        // Same "filed under the day it opened" fix as AppendTrade above.
        Append("Sim_Trades", entry, entryTime.Date);
    }

    private static void Append(string suffix, string entry, DateTime entryDate)
    {
        try
        {
            var dateStr = entryDate.ToString("yyyy_MM_dd");
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
