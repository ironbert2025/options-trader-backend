using System.Globalization;

namespace OptionsTrader.Infrastructure.Schwab;

// Appends every live price update (time + price) per symbol, one CSV per symbol per trading day
// (C:\OptionsData\{Symbol}_Ticks_{yyyyMMdd}.csv), for a later offline simulator (phase 2, not
// built yet — phase 1 here is just capturing the data starting now).
//
// Called from SchwabStreamerClient.HandleMessage on the instance actually connected to Schwab
// (the "hub" — see CandleHubServer/CandleHubClient in OptionsTrader.WinForms), AND from
// Form1.SetUpLiveFeedAsync on an instance connecting to a REMOTE hub on another machine (so that
// machine also builds up its own local tick history to run simulators against, since it never
// touches the hub machine's C:\OptionsData). NOT called for a client of a hub on the SAME
// machine — that machine's disk already got the row directly from the hub, writing it again from
// the client side would just duplicate it.
public static class TickPriceStore
{
    private const string OutputFolder = @"C:\OptionsData\MarketData\Ticks";
    private const string Header = "Time,Price";
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly object WriteLock = new();

    public static void Append(string symbol, DateTime utcTime, decimal price)
    {
        try
        {
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
            var symbolFolder = Path.Combine(OutputFolder, symbol);
            var path = Path.Combine(symbolFolder, $"{symbol}_Ticks_{eastern:yyyyMMdd}.csv");

            lock (WriteLock)
            {
                Directory.CreateDirectory(symbolFolder);
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
