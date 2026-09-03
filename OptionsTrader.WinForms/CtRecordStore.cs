using System.Text.Json;

namespace OptionsTrader.WinForms;

// One record PER T-Line ever drawn on panel 1 (Hora) or panel 2 (15Min), across ALL symbols — a
// single global, ever-growing file per PC (not rotated by day; Date lives as a FIELD on each
// record instead — per explicit request, "el archivo no es diario, es un archivo global donde la
// fecha es un campo más"). Created the moment the T-Line is drawn (Status "Pendiente"), then the
// SAME record is updated in place — never a new row — once it resolves (breaks Alza/Baja) or gets
// deleted before resolving. This is the source of truth CtLogWriter regenerates its .md from.
internal static class CtRecordStore
{
    private const string OutputFolder = @"C:\OptionsData\EventLog";
    private static string FilePath => Path.Combine(OutputFolder, $"ct_records_{Environment.MachineName}.json");

    public const string StatusPending           = "Pendiente";
    public const string StatusAlza               = "Alza";
    public const string StatusBaja                = "Baja";
    public const string StatusDeletedUnresolved  = "EliminadoSinResolver";

    public static List<CtRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<CtRecord>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<CtRecord>>(json) ?? new List<CtRecord>();
        }
        catch
        {
            return new List<CtRecord>();
        }
    }

    // Fired by every mutating method below (Add/Resolve/SetImagePath/MarkDeletedUnresolved) after
    // saving, so CtLogWriter can regenerate its .md from the fresh data — kept as an event (not a
    // direct call) so this store doesn't need to know CtLogWriter exists.
    public static event Action? OnChanged;

    public static void Add(string symbol, string timeframe, long t1, decimal p1, long t2, decimal p2, DateTime createdAt)
    {
        Mutate(records =>
        {
            records.Add(new CtRecord(symbol, timeframe, t1, p1, t2, p2, createdAt, StatusPending));
        });
    }

    // Marks the most recently-created still-Pendiente record for this exact T-Line as resolved —
    // updates it IN PLACE (never appends a new row), per explicit request: "una cosa es la
    // creación y otra es la ruptura".
    public static void Resolve(string symbol, string timeframe, long t1, decimal p1, long t2, decimal p2, string direction, DateTime resolvedAt)
    {
        Mutate(records =>
        {
            var idx = records.FindLastIndex(r => Matches(r, symbol, timeframe, t1, p1, t2, p2) && r.Status == StatusPending);
            if (idx < 0) return;
            records[idx] = records[idx] with { Status = direction, ResolvedAt = resolvedAt };
        });
    }

    // Called once the combined-chart screenshot for a just-resolved signal is actually captured
    // (asynchronous, happens in MultiChartForm/TwoPanelChartsControl's own Telegram push, which
    // only has the formatted caption text at that point — not the T-Line's own t1/p1/t2/p2). Just
    // takes the most recently RESOLVED record for this symbol+timeframe that doesn't have an image
    // yet — good enough since the image always arrives immediately after Resolve, in order; only a
    // rare same-instant multi-line resolve could theoretically mismatch, an acceptable tradeoff
    // versus threading the raw T-Line coordinates through OnTLineSignalEvent everywhere just for this.
    public static void SetImagePathForMostRecentResolved(string symbol, string timeframe, string imagePath)
    {
        Mutate(records =>
        {
            var idx = records.FindLastIndex(r => r.Symbol == symbol && r.Timeframe == timeframe
                && r.Status != StatusPending && r.Status != StatusDeletedUnresolved && r.ImagePath == null);
            if (idx < 0) return;
            records[idx] = records[idx] with { ImagePath = imagePath };
        });
    }

    // T-Line deleted (Delete key) while still Pendiente — per explicit request, NOT removed from
    // the log (would lose the fact the analysis was ever armed); marked instead so it stays visible
    // for later filtering/analysis.
    public static void MarkDeletedUnresolved(string symbol, string timeframe, long t1, decimal p1, long t2, decimal p2)
    {
        Mutate(records =>
        {
            var idx = records.FindLastIndex(r => Matches(r, symbol, timeframe, t1, p1, t2, p2) && r.Status == StatusPending);
            if (idx < 0) return;
            records[idx] = records[idx] with { Status = StatusDeletedUnresolved };
        });
    }

    private static bool Matches(CtRecord r, string symbol, string timeframe, long t1, decimal p1, long t2, decimal p2) =>
        r.Symbol == symbol && r.Timeframe == timeframe && r.T1 == t1 && r.P1 == p1 && r.T2 == t2 && r.P2 == p2;

    private static readonly object Lock = new();

    private static void Mutate(Action<List<CtRecord>> mutate)
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            lock (Lock)
            {
                var records = Load();
                mutate(records);
                var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                WithRetry(() => File.WriteAllText(FilePath, json));
            }
            OnChanged?.Invoke();
        }
        catch
        {
            // Best-effort, same as every other local store here.
        }
    }

    private static void WithRetry(Action action)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { action(); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch { return; }
        }
    }
}

// Date lives as CreatedAt (and ResolvedAt) on the record itself — the file/store is global, not
// split by day, per explicit request.
internal record CtRecord(
    string Symbol, string Timeframe, long T1, decimal P1, long T2, decimal P2,
    DateTime CreatedAt, string Status, DateTime? ResolvedAt = null, string? ImagePath = null);
