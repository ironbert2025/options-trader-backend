using System.Globalization;

namespace OptionsTrader.WinForms;

// Builds C:\OptionsData\IV_Historial_Apertura.csv — one ATM IV snapshot per symbol per day,
// taken from the first options-chain poll between 09:30:00 and 09:35:00 EST. Each open instance
// only writes the row for its own selected ticker (one symbol per instance, e.g. SPY vs QQQ) —
// it never touches another symbol's CSV files. Safe to call repeatedly; idempotent per day+symbol.
internal static class IvHistorialWriter
{
    private const string OutputFolder   = @"C:\OptionsData";
    private const string MasterFileName = "IV_Historial_Apertura.csv";
    private const string Header = "Fecha,Simbolo,HoraSnapshot,SpotPrice,StrikeATM_Call,IV_Call_ATM,StrikeATM_Put,IV_Put_ATM,IV_ATM_Promedio";

    private static readonly TimeOnly WindowStart = new(9, 30, 0);
    private static readonly TimeOnly WindowEnd   = new(9, 35, 0);

    // (date, symbol) pairs already confirmed recorded today — once set, TryAppendTodaysSnapshot
    // skips touching the CSV files entirely for the rest of the day instead of re-opening the
    // shared master file every 5 minutes just to find out it already has the row.
    private static readonly HashSet<(DateOnly Date, string Symbol)> _confirmedToday = new();

    public static void TryAppendTodaysSnapshot(string symbol, string expDateCode)
    {
        if (!MarketHours.IsWeekday || string.IsNullOrWhiteSpace(symbol)) return;

        var today = DateOnly.FromDateTime(MarketHours.NowEst);
        var key   = (today, symbol);
        if (_confirmedToday.Contains(key)) return;

        try
        {
            if (TryAppendSnapshot(symbol, ExpirationDateResolver.Resolve(expDateCode), today))
                _confirmedToday.Add(key);
        }
        catch
        {
            // Best-effort background job — a failure this cycle (e.g. file locked by another
            // instance writing the same symbol) is silently retried on the next scheduler tick.
        }
    }

    // Returns true once today's row for this symbol is confirmed present (either just written,
    // or already there from an earlier run today) — false means "not ready yet, keep polling"
    // (e.g. the underlying Call/Put CSVs don't exist yet or have no row in the ATM window).
    private static bool TryAppendSnapshot(string symbol, DateOnly expDate, DateOnly today)
    {
        var callPath = Path.Combine(OutputFolder, $"{symbol}_Call_{today:yyyyMMdd}_{expDate:yyyyMMdd}.csv");
        var putPath  = Path.Combine(OutputFolder, $"{symbol}_Put_{today:yyyyMMdd}_{expDate:yyyyMMdd}.csv");
        if (!File.Exists(callPath) || !File.Exists(putPath)) return false;

        var callAtm = FindAtmRow(callPath);
        var putAtm  = FindAtmRow(putPath);
        if (callAtm == null || putAtm == null) return false;

        var ivAvg = Math.Round((callAtm.Value.Iv + putAtm.Value.Iv) / 2, 4);
        var row = $"{today:yyyy-MM-dd},{symbol},{callAtm.Value.Time},{callAtm.Value.Spot},{callAtm.Value.Strike},{callAtm.Value.Iv},{putAtm.Value.Strike},{putAtm.Value.Iv},{ivAvg}";

        return AppendIfMissing(today, symbol, row);
    }

    private readonly record struct AtmRow(string Time, decimal Spot, decimal Strike, decimal Iv);

    // Reads the earliest snapshot (poll cycle) within the 09:30-09:35 window and returns the
    // strike closest to the spot price from that snapshot (the ATM strike).
    private static AtmRow? FindAtmRow(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2) return null;

        string? firstTimeInWindow = null;
        AtmRow? best = null;
        var bestDistance = decimal.MaxValue;

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 15) continue;
            if (!TimeOnly.TryParse(parts[0], out var time)) continue;
            if (time < WindowStart || time > WindowEnd) continue;

            firstTimeInWindow ??= parts[0];
            if (parts[0] != firstTimeInWindow) continue; // only the earliest snapshot in the window

            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var spot)) continue;
            if (!decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var strike)) continue;
            if (!decimal.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var iv)) continue;

            var distance = Math.Abs(strike - spot);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new AtmRow(parts[0], spot, strike, iv);
            }
        }

        return best;
    }

    // Exclusive file access for the check+append so two open instances can't race on the same row.
    // Returns true whether the row was already there or just got written — both mean "done for
    // today", which is what the in-memory _confirmedToday cache uses to stop re-checking the file.
    private static bool AppendIfMissing(DateOnly today, string symbol, string row)
    {
        Directory.CreateDirectory(OutputFolder);
        var path = Path.Combine(OutputFolder, MasterFileName);

        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var datePrefix = $"{today:yyyy-MM-dd},{symbol},";
        using (var reader = new StreamReader(stream, leaveOpen: true))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith(datePrefix, StringComparison.Ordinal)) return true; // already recorded today
            }
        }

        var isNewFile = stream.Length == 0;
        stream.Seek(0, SeekOrigin.End);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        if (isNewFile) writer.WriteLine(Header);
        writer.WriteLine(row);
        return true;
    }
}
