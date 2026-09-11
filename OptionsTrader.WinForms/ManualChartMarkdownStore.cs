namespace OptionsTrader.WinForms;

// Appends one entry (symbol + date/time + embedded image) to a FIXED-name markdown file — used by
// TwoPanelChartsControl's manual "Exp en 3"/"CT Hora"/"CT 15Min" buttons, per explicit request.
// Unlike EventLogMarkdownWriter (one file PER SYMBOL PER DAY, written automatically on live
// events), these are single shared files across every symbol/day, appended to only when the user
// manually clicks one of the 3 buttons — one file per button/topic, reviewed manually later.
internal static class ManualChartMarkdownStore
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";
    private const string ImageFolder = @"C:\OptionsTraderPush";

    // Saved under its own subfolder (one per button) so the 3 buttons' images never collide.
    public static string? SaveImage(string subFolder, string symbol, Bitmap image)
    {
        try
        {
            var folder = Path.Combine(ImageFolder, subFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{symbol}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static void AppendEntry(string fileName, string symbol, string? imagePath)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var path = Path.Combine(VaultFolder, fileName);

            var nl = Environment.NewLine;
            var imageLine = imagePath != null ? $"![Snapshot]({new Uri(imagePath).AbsoluteUri}){nl}{nl}" : string.Empty;
            var entry =
                $"### {symbol} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{nl}{nl}" +
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
            // Best-effort — a save failure here must never affect the chart the user was looking at.
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
