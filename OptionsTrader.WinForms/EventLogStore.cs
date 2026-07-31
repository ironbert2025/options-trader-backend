namespace OptionsTrader.WinForms;

// Cumulative CSV log of every chart-analysis event (Cross-SMA, T-Line+SMA20 breakout, Daily
// bounce, and whatever else gets added later) across all symbols, for offline analysis in Excel.
// One instance runs per ticker as a SEPARATE OS process, all appending to this SAME file — same
// exclusive-lock-with-retry pattern as OpenTradesStore, since a plain append from multiple
// processes at once can interleave/corrupt lines.
internal static class EventLogStore
{
    private const string OutputFolder = @"C:\OptionsData\EventLog";
    private static readonly string FilePath = Path.Combine(OutputFolder, "events_log.csv");
    private const string Header = "Fecha,Hora,Symbolo,Timeframe,EventType,Direccion,Descripcion,Precio,Referencia";

    public static void Append(string symbol, string timeframe, string eventType, string direccion,
        string descripcion, decimal precio, string referencia = "")
    {
        Directory.CreateDirectory(OutputFolder);
        var now = DateTime.Now;
        var line = string.Join(",",
            now.ToString("yyyy-MM-dd"),
            now.ToString("HH:mm:ss"),
            Csv(symbol),
            Csv(timeframe),
            Csv(eventType),
            Csv(direccion),
            Csv(descripcion),
            precio.ToString("F2"),
            Csv(referencia));

        WithRetry(() =>
        {
            var isNew = !File.Exists(FilePath);
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            if (isNew) writer.WriteLine(Header);
            writer.WriteLine(line);
        });
    }

    // Wraps a field in quotes only if it needs it — Descripcion/Referencia are built from fixed
    // templates that never contain a raw comma today, but this stays safe if that changes.
    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    // Same retry rationale as OpenTradesStore: another instance can briefly hold the exclusive
    // lock while this one tries to append — retry a few times instead of losing the event.
    private static void WithRetry(Action action)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { action(); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch { return; } // not a locking issue — best-effort logging, don't throw into caller
        }
    }
}
