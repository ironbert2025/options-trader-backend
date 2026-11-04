using System.Globalization;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// 1-minute candles rebuilt from the Level 1 tick files saved on disk (TicksLevelOne\SYM\SYM_L1Ticks_yyyyMMdd.csv,
// falling back to Ticks\SYM\SYM_Ticks_yyyyMMdd.csv) — no REST request. Used by the Daily form's candle-detail window.
// File times are Eastern wall-clock time-of-day; the date comes from the file name.
internal static class TickFileMinuteCandles
{
    private const string TicksLevelOneFolder = @"C:\OptionsData\MarketData\TicksLevelOne";
    private const string TicksFolder = @"C:\OptionsData\MarketData\Ticks";
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // Past days never change: kept in memory once read. Today's file keeps growing, so it is re-read.
    private static readonly Dictionary<(string, DateOnly), List<CandleData>> PastDays = new();

    // 1-minute candles in [startUtc, endUtc) for the symbol; empty when that day has no tick file.
    public static List<CandleData> Get(string symbol, DateTime startUtc, DateTime endUtc)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, EasternZone));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));

        List<CandleData> ticks;
        lock (PastDays)
        {
            if (day < today && PastDays.TryGetValue((symbol, day), out var cached)) ticks = cached;
            else
            {
                ticks = LoadTicks(symbol, day);
                if (day < today && ticks.Count > 0) PastDays[(symbol, day)] = ticks;
            }
        }
        return MinuteCandleStore.FromTicks(ticks, startUtc, endUtc, DateTime.MaxValue);
    }

    // Median High-Low range of the first 15m candle (09:30-09:45 ET) over up to maxDays of the days BEFORE beforeDay that
    // have a tick file — the wick floor of OpenRevisitDetector. Null when fewer than 5 days are available. Reads whole
    // tick files (cached once past), so call it off the UI/feed thread.
    public static decimal? MedianFirstCandleRange(string symbol, DateOnly beforeDay, int maxDays = 20)
    {
        var ranges = new List<decimal>();
        for (var back = 1; back <= 45 && ranges.Count < maxDays; back++)
        {
            var day = beforeDay.AddDays(-back);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(9, 30)), DateTimeKind.Unspecified), EasternZone);
            var minutes = Get(symbol, startUtc, startUtc.AddMinutes(15));
            if (minutes.Count < 5) continue; // no file (holiday) or an almost empty one
            ranges.Add(minutes.Max(c => c.High) - minutes.Min(c => c.Low));
        }
        return OpenRevisitDetector.MedianRange(ranges);
    }

    private static List<CandleData> LoadTicks(string symbol, DateOnly day)
    {
        var dateStr = day.ToString("yyyyMMdd");
        var path = Path.Combine(TicksLevelOneFolder, symbol, $"{symbol}_L1Ticks_{dateStr}.csv");
        if (!File.Exists(path)) path = Path.Combine(TicksFolder, symbol, $"{symbol}_Ticks_{dateStr}.csv");
        var result = new List<CandleData>();
        if (!File.Exists(path)) return result;

        string[] lines;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            var all = new List<string>();
            while (reader.ReadLine() is { } line) all.Add(line);
            lines = all.ToArray();
        }
        catch { return result; }

        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;
            if (!TimeSpan.TryParse(parts[0], CultureInfo.InvariantCulture, out var tod))
            {
                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var old)) continue;
                tod = old.TimeOfDay;
            }
            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0) continue;

            var eastern = day.ToDateTime(TimeOnly.MinValue) + tod;
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), EasternZone);
            result.Add(new CandleData { Time = utc, Open = price, High = price, Low = price, Close = price });
        }
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }
}
