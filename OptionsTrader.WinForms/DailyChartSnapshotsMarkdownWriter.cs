namespace OptionsTrader.WinForms;

// Builds ONE daily markdown note combining every ticker's Open/Close chart snapshots (see
// Form1.CaptureOpenCloseSnapshotsAsync, which each of the 4 ticker processes already saves to
// C:\OptionsData\ChartSnapshots\{symbol}\{symbol}_{yyyyMMdd}_{Open|Close}.png) — only the primary
// (first) ticker instance calls this (see Form1.IsPrimaryTickerInstance), so there's exactly one
// combined file per day instead of each process writing a redundant copy. Same vault/folder and
// "one file per day per PC" convention as DailyTradeLogWriter/EventLogMarkdownWriter, but a
// separate _OpenClose file so it doesn't mix with per-trade or per-event entries.
internal static class DailyChartSnapshotsMarkdownWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";
    private const string SnapshotsFolder = @"C:\OptionsData\ChartSnapshots";

    // All 4 ticker processes trigger their own Open/Close capture at the same wall-clock window
    // (MarketHours.IsInMarketCloseSnapshotWindow, 16:00-16:01 ET) with NO coordination between
    // them — this instance's own Close.png can land before or after the other 3 symbols'. Rather
    // than checking each symbol once and moving on, poll ALL of them together every 5s and only
    // proceed once every symbol's Close.png exists — i.e. wait for the minimum of "all 4 are
    // actually done" — up to a hard cap (5 minutes) so a crashed/closed sibling process can't
    // block this file forever; whatever's still missing at the cap just gets skipped.
    private const int MaxPollCycles = 60; // 60 * 5s = 5 minutes
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(5);

    public static async Task WriteAsync(IReadOnlyList<string> symbols, DateTime dateEst)
    {
        try
        {
            var dateTag = dateEst.ToString("yyyyMMdd");

            (string Symbol, string OpenPath, string ClosePath) PathsFor(string symbol)
            {
                var folder = Path.Combine(SnapshotsFolder, symbol);
                return (symbol,
                    Path.Combine(folder, $"{symbol}_{dateTag}_Open.png"),
                    Path.Combine(folder, $"{symbol}_{dateTag}_Close.png"));
            }

            var entries = symbols.Select(PathsFor).ToList();

            for (var cycle = 0; cycle < MaxPollCycles; cycle++)
            {
                if (entries.All(e => File.Exists(e.ClosePath))) break;
                await Task.Delay(PollDelay);
            }

            var images = entries
                .Select(e => (
                    e.Symbol,
                    Open: File.Exists(e.OpenPath) ? e.OpenPath : null,
                    Close: File.Exists(e.ClosePath) ? e.ClosePath : null))
                .ToList();

            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{dateEst:yyyy_MM_dd}_{Environment.MachineName}_OpenClose.md";
            var path = Path.Combine(VaultFolder, fileName);
            var nl = Environment.NewLine;

            var body = new System.Text.StringBuilder();
            body.Append($"# Chart Snapshots — {dateEst:yyyy-MM-dd}{nl}{nl}");
            foreach (var (symbol, openPath, closePath) in images)
            {
                body.Append($"## {symbol}{nl}{nl}");
                if (openPath != null)
                    body.Append($"**Open**{nl}![Open]({new Uri(openPath).AbsoluteUri}){nl}{nl}");
                if (closePath != null)
                    body.Append($"**Close**{nl}![Close]({new Uri(closePath).AbsoluteUri}){nl}{nl}");
                if (openPath == null && closePath == null)
                    body.Append($"_(sin imágenes hoy){nl}{nl}");
                body.Append($"---{nl}{nl}");
            }

            WithRetry(() => File.WriteAllText(path, body.ToString()));
        }
        catch
        {
            // Best-effort, same as every other local store here — never let this affect the
            // market-close flow that just finished.
        }
    }

    private static void WithRetry(Action action)
    {
        for (var attempt = 0; attempt < 20; attempt++)
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
