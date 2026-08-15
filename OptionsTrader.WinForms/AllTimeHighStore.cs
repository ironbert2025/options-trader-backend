using System.Text.Json;

namespace OptionsTrader.WinForms;

// Persists every symbol's All-Time High in ONE shared file, alongside the rest of the app's UI
// settings (%AppData%\OptionsTrader\ — same folder as TickerSettingsStore/BalanceStore/etc.),
// keyed by symbol so one file covers every ticker instead of a file per symbol. Updated once per
// day, at the RTH close (16:00 ET) — see ChartPanel.EvaluateAllTimeHighAtClose — not on every
// tick, so a bad print during the day can't corrupt it.
//
// Multiple ticker processes can each try to save their own symbol's entry around the same close
// time — Save() takes a cross-process Mutex around its read-modify-write so one process's update
// can't clobber another's.
internal static class AllTimeHighStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader",
        "all_time_highs.json");

    private const string MutexName = "Global\\OptionsTrader_AllTimeHighs_Mutex";

    private sealed class AllTimeHighRecord
    {
        public decimal Value { get; set; }
        public DateOnly Date { get; set; }
    }

    private static Dictionary<string, AllTimeHighRecord> LoadAll()
    {
        if (!File.Exists(FilePath)) return new();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, AllTimeHighRecord>>(json) ?? new();
        }
        catch
        {
            // Corrupt/partial file — treat as empty rather than crash the chart.
            return new();
        }
    }

    public static (decimal Value, DateOnly Date)? Load(string symbol)
    {
        var all = LoadAll();
        return all.TryGetValue(symbol, out var record) ? (record.Value, record.Date) : null;
    }

    public static void Save(string symbol, decimal value, DateOnly date)
    {
        using var mutex = new Mutex(false, MutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; } // previous owner crashed — safe to proceed

            var all = LoadAll();
            all[symbol] = new AllTimeHighRecord { Value = value, Date = date };

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
