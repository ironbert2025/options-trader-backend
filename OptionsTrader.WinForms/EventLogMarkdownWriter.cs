namespace OptionsTrader.WinForms;

// Appends one markdown entry per event notification actually pushed to Telegram (Cross-SMA,
// T-Line+SMA20, Demand Zone rebote, Piso/Techo, Abriendo la Volatilidad) to a daily Obsidian note
// — same vault/folder and "one file per day per PC" convention as DailyTradeLogWriter, but a
// separate _EventLogs file so trade open/close entries and event notifications don't mix.
// Live app only — the simulator never pushes to Telegram (log-only), so it never calls this.
internal static class EventLogMarkdownWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    // imagePath is the same local PNG the caller already saved before pushing to Telegram —
    // referenced here via file:// instead of re-uploaded/copied into the vault.
    public static void AppendEvent(string symbol, string caption, string imagePath)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{DateTime.Now:yyyy_MM_dd}_{Environment.MachineName}_EventLogs.md";
            var path = Path.Combine(VaultFolder, fileName);

            var time = DateTime.Now.ToString("HH:mm:ss");
            var nl   = Environment.NewLine;
            var imageUri = new Uri(imagePath).AbsoluteUri;
            var entry =
                $"### {symbol} — {time}{nl}{nl}" +
                $"{caption}{nl}{nl}" +
                $"![Event]({imageUri}){nl}{nl}" +
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
