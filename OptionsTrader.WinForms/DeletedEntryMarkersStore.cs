using System.Text.Json;

namespace OptionsTrader.WinForms;

// Entry-spot rayitas (the short white/yellow line drawn when a trade opens) the user deleted from
// the chart with the Delete key. A still-open trade's entry line is re-drawn from OpenTradesStore
// every time a chart loads (ChartPanel.ReplayPersistedEntryMarkersAsync), so without remembering
// the deletion it would just come back on the next open. Keyed by (symbol, entry spot price, day of
// the trade) — precise enough that a NEW trade at the same price on another day isn't suppressed.
// Entries older than 14 days are dropped on every write (a trade open that long is long gone).
internal static class DeletedEntryMarkersStore
{
    private record Entry(string Symbol, decimal Price, DateOnly Day);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "deleted_entry_markers.json");

    private static List<Entry> LoadAll()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); } // corrupt file — never block chart loading
    }

    public static bool IsDeleted(string symbol, decimal price, DateOnly day) =>
        LoadAll().Any(e => e.Symbol == symbol && e.Price == price && e.Day == day);

    public static void Add(string symbol, decimal price, DateOnly day)
    {
        try
        {
            var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-14));
            var all = LoadAll().Where(e => e.Day >= cutoff).ToList();
            if (!all.Any(e => e.Symbol == symbol && e.Price == price && e.Day == day))
                all.Add(new Entry(symbol, price, day));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch { /* best-effort — worst case the rayita reappears on next load */ }
    }
}
