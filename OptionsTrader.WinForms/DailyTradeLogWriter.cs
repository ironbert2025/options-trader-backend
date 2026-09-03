namespace OptionsTrader.WinForms;

// Appends one markdown entry per closed trade (demo or real, manual or automatic close — the
// single call site is Form1.CloseTradeRowAsync, the convergence point for all of them) to a daily
// Obsidian note. One file per day PER PC — multiple machines can run instances (see the Hub Host /
// Token Share multi-PC setup), so the PC name in the filename keeps each machine's trades in its
// own file instead of racing to write the same one. Creates the folder tree if it doesn't exist.
internal static class DailyTradeLogWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    public static void AppendTrade(TradeRecord trade)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{DateTime.Now:yyyy_MM_dd}_{Environment.MachineName}_Trades.md";
            var path = Path.Combine(VaultFolder, fileName);

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
