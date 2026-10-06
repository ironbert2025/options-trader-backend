namespace OptionsTrader.WinForms;

// One "Cargar" of the Simulator = one run: where its combined panel-1+2 screenshots go and which
// markdown note references them. Each Cargar gets its OWN image folder (timestamped), so reloading the
// same data day never overwrites images an earlier note still points at.
internal sealed class SimPriceActionRun
{
    public required string Symbol { get; init; }
    public required DateOnly DataDate { get; init; }
    public required DateTime Started { get; init; }
    public required string MdPath { get; init; }
    public required string ImageFolder { get; init; }
    public string HeaderLine { get; init; } = string.Empty;
    public string ReloadLabel { get; init; } = "Recarga"; // separator word when this run appends to an existing note
    public bool HeaderDone { get; set; }
}

// Simulator "RTH Price Action" note (..._RTH_PriceAction.md): the combined panel 1 + panel 2 screenshot
// at the close of every 15m candle, plus one at each Bollinger-momentum event (Validación / Toque).
// Candle-close entries sit at the left margin (the vertical line of images); event entries are
// indented to the right so they stand out. Append-only — a replay takes ~6.5 h, so entries are
// written as they happen. The name carries symbol + data date so it never clashes with the live
// app's daily "{date}_{PC}_RTH_PriceAction.md" (see DailyChartSnapshotsMarkdownWriter).
internal static class SimPriceActionMarkdownWriter
{
    private const string VaultFolder = @"C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades";
    private const string ImagesRoot = @"C:\OptionsData\Simulator\PriceAction";

    // Inline em-spaces in front of the label/image: plain Markdown + HTML entities, so the shift to the
    // right survives any renderer (a <div> wrapper around Markdown is not reliable in Obsidian). Tune here.
    private static readonly string EventIndent = string.Concat(Enumerable.Repeat("&emsp;", 6));

    public static SimPriceActionRun CreateRun(string symbol, DateOnly dataDate, DateOnly? exp)
    {
        var started = DateTime.Now;
        var runDateStr = started.ToString("yyyy_MM_dd");
        var expPart = exp.HasValue ? $"_exp{exp.Value:yyyyMMdd}" : string.Empty;
        var fileName = $"{runDateStr}_{Environment.MachineName}_{symbol}_Sim_{dataDate:yyyy_MM_dd}{expPart}_RTH_PriceAction.md";
        return new SimPriceActionRun
        {
            Symbol = symbol,
            DataDate = dataDate,
            Started = started,
            MdPath = Path.Combine(VaultFolder, runDateStr, fileName),
            ImageFolder = Path.Combine(ImagesRoot, symbol, dataDate.ToString("yyyyMMdd"), started.ToString("yyyyMMdd_HHmmss")),
            HeaderLine = $"# {symbol} — Price Action RTH (simulación) — datos {dataDate:yyyy-MM-dd}{(exp.HasValue ? $" · exp {exp.Value:yyyy-MM-dd}" : string.Empty)}"
        };
    }

    // Live Charts tab: ONE note per symbol per trading day ({date}_{PC}_{SYMBOL}_RTH_PriceAction.md), kept
    // apart from the all-tickers Open/Close note (DailyChartSnapshotsMarkdownWriter), which is rewritten
    // whole at the close and would erase these entries. Images sit with the other live snapshots.
    public static SimPriceActionRun CreateLiveRun(string symbol, DateOnly day)
    {
        var started = DateTime.Now;
        var dayStr = day.ToString("yyyy_MM_dd");
        return new SimPriceActionRun
        {
            Symbol = symbol,
            DataDate = day,
            Started = started,
            MdPath = Path.Combine(VaultFolder, dayStr, $"{dayStr}_{Environment.MachineName}_{symbol}_RTH_PriceAction.md"),
            ImageFolder = Path.Combine(@"C:\OptionsData\ChartSnapshots", symbol, "PriceAction", day.ToString("yyyyMMdd")),
            HeaderLine = $"# {symbol} — Price Action RTH — {day:yyyy-MM-dd}",
            ReloadLabel = "Reinicio"
        };
    }

    private static string ImageMarkdown(string imagePath) => $"![]({new Uri(imagePath).AbsoluteUri})";

    // Candle close: label (just the time) + image, left margin.
    public static void AppendCandleClose(SimPriceActionRun run, string timeLabel, string? imagePath, string? noteWhenNoImage = null) =>
        Append(run, imagePath != null
            ? $"**{timeLabel}**\n\n{ImageMarkdown(imagePath)}\n\n"
            : $"**{timeLabel}** _({noteWhenNoImage ?? "sin imagen: captura no disponible"})_\n\n");

    // Event: label (time + description) + image, indented. note replaces the image when there is none.
    public static void AppendEvent(SimPriceActionRun run, string label, string? imagePath, string? noteWhenNoImage) =>
        Append(run, imagePath != null
            ? $"{EventIndent}**{label}**\n\n{EventIndent}{ImageMarkdown(imagePath)}\n\n"
            : $"{EventIndent}**{label}** _({noteWhenNoImage ?? "sin imagen"})_\n\n");

    public static void AppendNote(SimPriceActionRun run, string note) => Append(run, $"_{note}_\n\n");

    private static void Append(SimPriceActionRun run, string block)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(run.MdPath)!);
            var isNew = !File.Exists(run.MdPath);
            var prefix = string.Empty;
            if (!run.HeaderDone)
            {
                run.HeaderDone = true;
                // First write of this Cargar: file header if the note is new, otherwise a reload separator.
                prefix = isNew
                    ? $"{run.HeaderLine}\n\nCorrida: {run.Started:yyyy-MM-dd HH:mm:ss}\n\n---\n\n"
                    : $"---\n\n_{run.ReloadLabel} {run.Started:HH:mm:ss}_\n\n";
            }
            var text = (prefix + block).Replace("\n", Environment.NewLine);

            WithRetry(() =>
            {
                using var stream = new FileStream(run.MdPath, FileMode.Append, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(text);
            });
        }
        catch
        {
            // Best-effort, same as every other local store here — never let this affect playback.
        }
    }

    private static void WithRetry(Action action)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { action(); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch { return; }
        }
    }
}
