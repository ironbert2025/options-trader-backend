using System.Globalization;

namespace OptionsTrader.Infrastructure.Schwab;

// Appends every raw LEVEL_ONE_EQUITIES last-price update (time + price) per symbol, one CSV per
// symbol per trading day (C:\OptionsData\MarketData\TicksLevelOne\{Symbol}\{Symbol}_L1Ticks_
// {yyyyMMdd}.csv) — a higher-frequency counterpart to TickPriceStore's one-per-minute
// CHART_EQUITY samples, kept side by side so the two can be compared against real (e.g.
// ThinkorSwim) prices to decide which source drives the live chart better.
//
// Same single-writer guarantee as TickPriceStore: only ever called from SchwabStreamerClient on
// the "hub" instance actually connected to Schwab.
internal static class LevelOneTickStore
{
    private const string OutputFolder = @"C:\OptionsData\MarketData\TicksLevelOne";
    private const string Header = "Time,Price";
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly object WriteLock = new();

    public static void Append(string symbol, DateTime utcTime, decimal price)
    {
        try
        {
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
            var symbolFolder = Path.Combine(OutputFolder, symbol);
            var path = Path.Combine(symbolFolder, $"{symbol}_L1Ticks_{eastern:yyyyMMdd}.csv");

            lock (WriteLock)
            {
                Directory.CreateDirectory(symbolFolder);
                var isNew = !File.Exists(path);
                using var writer = new StreamWriter(path, append: true);
                if (isNew) writer.WriteLine(Header);
                // Millisecond precision (unlike TickPriceStore's whole-second timestamps) — this
                // feed can update several times per second.
                writer.WriteLine($"{eastern:yyyy-MM-dd HH:mm:ss.fff},{price.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        catch
        {
            // Best-effort logging — never let this break the receive loop.
        }
    }
}
