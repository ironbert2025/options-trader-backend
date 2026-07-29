using System.Globalization;

namespace OptionsTrader.Infrastructure.Schwab;

// Appends every live price update (time + price) per symbol, one CSV per symbol per trading day
// (C:\OptionsData\{Symbol}_Ticks_{yyyyMMdd}.csv), for a later offline simulator (phase 2, not
// built yet — phase 1 here is just capturing the data starting now).
//
// Only ever called from SchwabStreamerClient.HandleMessage on the instance that's actually
// connected to Schwab (the "hub" — see CandleHubServer/CandleHubClient in OptionsTrader.WinForms).
// Other app instances relay candles via CandleHubClient instead, which never touches this, so
// there's no risk of multiple processes writing duplicate rows for the same symbol/tick.
internal static class TickPriceStore
{
    private const string OutputFolder = @"C:\OptionsData";
    private const string Header = "Time,Price";
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly object WriteLock = new();

    public static void Append(string symbol, DateTime utcTime, decimal price)
    {
        try
        {
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
            var path = Path.Combine(OutputFolder, $"{symbol}_Ticks_{eastern:yyyyMMdd}.csv");

            lock (WriteLock)
            {
                Directory.CreateDirectory(OutputFolder);
                var isNew = !File.Exists(path);
                using var writer = new StreamWriter(path, append: true);
                if (isNew) writer.WriteLine(Header);
                writer.WriteLine($"{eastern:yyyy-MM-dd HH:mm:ss},{price.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        catch
        {
            // Best-effort logging — never let this break the receive loop.
        }
    }
}
