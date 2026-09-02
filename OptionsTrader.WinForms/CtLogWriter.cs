namespace OptionsTrader.WinForms;

// Renders CtRecordStore's records into a single GLOBAL markdown file (not rotated by day — Date is
// a field on each record, per explicit request), one file per PC. Regenerated in full every time
// CtRecordStore changes (creation, resolution, image attached, or deleted-unresolved) — a record is
// UPDATED in place there, so this always reflects current state instead of accumulating duplicate
// entries the way a plain append-only writer would. Subscribes to CtRecordStore.OnChanged once,
// wired up from Form1's own startup (see WireUp below) so every mutation anywhere in the app
// (any symbol, any panel) keeps this file in sync automatically.
internal static class CtLogWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";
    private static string FilePath => Path.Combine(VaultFolder, $"{Environment.MachineName}_CT.md");

    private static bool _wired;

    // Call once at app startup (idempotent) — subscribes CtRecordStore's change notifications to
    // trigger a regenerate, and does an initial regenerate so the file reflects whatever's already
    // on disk even before the next mutation.
    public static void WireUp()
    {
        if (_wired) return;
        _wired = true;
        CtRecordStore.OnChanged += Regenerate;
        Regenerate();
    }

    private static readonly object Lock = new();

    public static void Regenerate()
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var records = CtRecordStore.Load()
                .OrderBy(r => r.CreatedAt)
                .ToList();

            var nl = Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append($"# CT — T-Line + SMA20 breakout log ({Environment.MachineName}){nl}{nl}");
            sb.Append($"_Regenerado automáticamente — {records.Count} registros._{nl}{nl}");

            foreach (var r in records)
            {
                var statusLabel = r.Status switch
                {
                    CtRecordStore.StatusPending             => "⏳ Pendiente (armado, sin resolver)",
                    CtRecordStore.StatusAlza                 => "✅ CT al Alza",
                    CtRecordStore.StatusBaja                  => "✅ CT a la Baja",
                    CtRecordStore.StatusDeletedUnresolved    => "🗑️ Eliminado sin resolver",
                    _ => r.Status
                };

                sb.Append($"### {r.Symbol} — {r.Timeframe} — {r.CreatedAt:yyyy-MM-dd HH:mm:ss}{nl}{nl}");
                sb.Append($"Estado: {statusLabel}{nl}{nl}");
                if (r.ResolvedAt.HasValue)
                    sb.Append($"Resuelto: {r.ResolvedAt.Value:yyyy-MM-dd HH:mm:ss}{nl}{nl}");
                if (r.ImagePath != null)
                    sb.Append($"![CT]({new Uri(r.ImagePath).AbsoluteUri}){nl}{nl}");
                sb.Append($"---{nl}{nl}");
            }

            lock (Lock)
            {
                WithRetry(() => File.WriteAllText(FilePath, sb.ToString()));
            }
        }
        catch
        {
            // Best-effort, same as every other local store here — never let this affect the
            // detection/drawing flow that triggered it.
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
