namespace OptionsTrader.WinForms;

// Appends one markdown entry per "CT" (T-Line + SMA20 breakout) signal to a GENERAL daily Obsidian
// note — unlike EventLogMarkdownWriter (per-symbol, mixes every event type together), this one
// file per day/PC collects CT entries from EVERY symbol's panel 1 (Hora) and panel 2 (15Min)
// together, per explicit request. Only ever called after a successful Telegram push (same
// screenshot that push already captured/sent) from MultiChartForm/TwoPanelChartsControl's own
// SendTLineSignalTelegramPushAsync — so an entry here always has Date, Symbol, TimeFrame, and the
// combined-chart image.
internal static class CtLogWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    public static void AppendEntry(string symbol, string timeframe, string caption, string imagePath)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{DateTime.Now:yyyy_MM_dd}_{Environment.MachineName}_CT.md";
            var path = Path.Combine(VaultFolder, fileName);

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var time = DateTime.Now.ToString("HH:mm:ss");
            var nl   = Environment.NewLine;
            var entry =
                $"### {symbol} — {timeframe} — {date} {time}{nl}{nl}" +
                $"{caption}{nl}{nl}" +
                $"![CT]({new Uri(imagePath).AbsoluteUri}){nl}{nl}" +
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
            // Telegram push flow that just finished.
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
