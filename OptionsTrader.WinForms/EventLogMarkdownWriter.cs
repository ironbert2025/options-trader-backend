namespace OptionsTrader.WinForms;

// Appends one markdown entry per event notification to a daily Obsidian note — same vault/folder
// and "one file per day per PC" convention as DailyTradeLogWriter, but a separate _EventLogs file
// so trade open/close entries and event notifications don't mix. Most callers only call this after
// a successful Telegram push (Cross-SMA, T-Line+SMA20, Demand Zone rebote, Piso/Techo, Abriendo la
// Volatilidad), embedding that same screenshot — but it's also called directly for events with no
// screenshot/Telegram push of their own (Abriendo Bollinger), which just omit imagePath.
// Live app only — the simulator never pushes to Telegram (log-only), so it never calls this.
internal static class EventLogMarkdownWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    // imagePath is the same local PNG the caller already saved before pushing to Telegram —
    // referenced here via file:// instead of re-uploaded/copied into the vault. Optional — events
    // that never generate a screenshot (e.g. "Abriendo Bollinger", which has no Telegram push of
    // its own) pass null and just get the text entry, no image line.
    public static void AppendEvent(string symbol, string caption, string? imagePath = null)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{DateTime.Now:yyyy_MM_dd}_{Environment.MachineName}_EventLogs.md";
            var path = Path.Combine(VaultFolder, fileName);

            var time = DateTime.Now.ToString("HH:mm:ss");
            var nl   = Environment.NewLine;
            var imageLine = imagePath != null ? $"![Event]({new Uri(imagePath).AbsoluteUri}){nl}{nl}" : string.Empty;
            var entry =
                $"### {symbol} — {time}{nl}{nl}" +
                $"{caption}{nl}{nl}" +
                imageLine +
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
