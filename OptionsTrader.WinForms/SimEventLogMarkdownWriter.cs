namespace OptionsTrader.WinForms;

// Simulator equivalent of EventLogMarkdownWriter — every message SimulatorForm.LogSimEvent
// already shows in the on-screen log (T-Line, Cross-SMA, DZ/SZ rebote, Piso/Techo, Abriendo la
// Volatilidad, Daily Bounce, and manual trade open/close lines) also gets appended here, so a
// replay leaves a permanent record instead of vanishing the moment the Simulator window closes.
//
// Distinct from the live app's file in 2 ways, per explicit request:
//   - "_Sim" in the filename, so it never gets confused with a real live session's log.
//   - BOTH dates are tracked: rundate (today, when this replay was actually run) and datadate
//     (the historical day whose recorded ticks were loaded) — a symbol can be replayed many times
//     on different days against the same datadate, and each run gets its own file.
internal static class SimEventLogMarkdownWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";

    public static void AppendEvent(string symbol, DateOnly runDate, DateOnly dataDate, DateTime simulatedTime, string caption)
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var fileName = $"{runDate:yyyy_MM_dd}_{Environment.MachineName}_{symbol}_Sim_{dataDate:yyyy_MM_dd}_EventLogs.md";
            var path = Path.Combine(VaultFolder, fileName);

            var nl = Environment.NewLine;
            var entry =
                $"### {symbol} — {simulatedTime:HH:mm:ss}{nl}{nl}" +
                $"{caption}{nl}{nl}" +
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
            // simulator's own playback/rendering.
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
