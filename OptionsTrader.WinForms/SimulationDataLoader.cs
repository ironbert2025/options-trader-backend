using System.Globalization;
using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.WinForms;

// One frozen-in-time snapshot the simulator can step to — the options-chain poll cycle (Call +
// Put quotes) and the underlying price closest to that moment, both read from files the app
// already wrote while running live that day.
public sealed class SimulationStep
{
    public DateTime Time { get; init; }
    public List<OptionQuoteDto> Quotes { get; init; } = new();
    public decimal UnderlyingPrice { get; init; }
}

// Reads whatever CsvLogger / TickPriceStore / LevelOneTickStore already saved for a given
// (symbol, day) into an in-memory replay timeline. READ-ONLY — never writes to any of those
// files, and completely separate from the live polling/streaming code paths (ChartPanel,
// SchwabStreamerClient, Form1's polling loop are never touched by this class).
internal static class SimulationDataLoader
{
    private const string IvFolder            = @"C:\OptionsData\Trades\Iv";
    private const string TicksFolder         = @"C:\OptionsData\MarketData\Ticks";
    private const string TicksLevelOneFolder = @"C:\OptionsData\MarketData\TicksLevelOne";

    // Days that actually have recorded options-chain data for this symbol, newest first — used
    // to limit the simulator's date picker to dates that can actually be replayed.
    public static List<DateOnly> GetAvailableDates(string symbol)
    {
        if (!Directory.Exists(IvFolder)) return new();

        var dates = new HashSet<DateOnly>();
        foreach (var file in Directory.EnumerateFiles(IvFolder, $"{symbol}_Call_*.csv"))
        {
            var name  = Path.GetFileNameWithoutExtension(file); // {Symbol}_Call_{yyyyMMdd}_{expDate}
            var parts = name.Split('_');
            if (parts.Length >= 3 &&
                DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                dates.Add(DateOnly.FromDateTime(d));
        }
        return dates.OrderByDescending(d => d).ToList();
    }

    // Builds the full step timeline for one symbol/day. Steps are the options-chain poll
    // snapshots (Call+Put grouped by Time) — the finest granularity actually recorded.
    public static List<SimulationStep> LoadDay(string symbol, DateOnly date)
    {
        var dateStr = date.ToString("yyyyMMdd");

        // If both the current AND next expiration were logged that day, prefer the nearer
        // expiration (smaller date suffix) — same one the live grid shows by default.
        var callFile = Directory.EnumerateFiles(IvFolder, $"{symbol}_Call_{dateStr}_*.csv").OrderBy(f => f).FirstOrDefault();
        var putFile  = Directory.EnumerateFiles(IvFolder, $"{symbol}_Put_{dateStr}_*.csv").OrderBy(f => f).FirstOrDefault();

        var byTime = new SortedDictionary<TimeOnly, List<OptionQuoteDto>>();
        if (callFile != null) ReadQuotesInto(callFile, symbol, OptionType.Call, byTime);
        if (putFile  != null) ReadQuotesInto(putFile,  symbol, OptionType.Put,  byTime);

        var prices = LoadUnderlyingTicks(symbol, date);

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var steps = new List<SimulationStep>();
        foreach (var (time, quotes) in byTime)
        {
            // Step.Time is stored as real UTC (same convention as CandleData.Time) so it can be
            // compared directly against _underlyingCandles in SimulatorForm — the CSV's own Time
            // column is Eastern wall-clock, converted here just like TickPriceStore's rows are.
            var stepTimeEastern = date.ToDateTime(time);
            var stepTimeUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(stepTimeEastern, DateTimeKind.Unspecified), eastern);
            steps.Add(new SimulationStep
            {
                Time            = stepTimeUtc,
                Quotes          = quotes,
                UnderlyingPrice = NearestPriceAt(prices, stepTimeEastern) ?? quotes.FirstOrDefault()?.SpotPrice ?? 0m
            });
        }
        return steps;
    }

    // Raw underlying price ticks for the day, as "flat" candles (Open=High=Low=Close=price) so
    // they can be fed straight into CandleAggregation.AggregateToInterval — the same aggregation
    // the live chart uses, just replaying pre-recorded prices instead of live ones. Prefers
    // LevelOneTickStore (finer resolution) when present for that day, falls back to TickPriceStore.
    private static List<CandleData> LoadUnderlyingCandlesForDay(string symbol, DateOnly date) =>
        LoadUnderlyingTicks(symbol, date)
            .Select(t => new CandleData { Time = t.Time, Open = t.Price, High = t.Price, Low = t.Price, Close = t.Price })
            .ToList();

    // Same idea as the live chart's "days visible" default zoom (7 days for the 1h panel, 3 for
    // the 15m panels) — the simulator should show that same amount of surrounding context, not
    // just the single replayed day, so it reads the same as a real live chart. Prior days come
    // from whichever tick files happen to exist for this symbol; if capture only started
    // recently there may be fewer than requested, which is a genuine data limit, not a bug.
    public static List<CandleData> LoadUnderlyingCandlesWithContext(string symbol, DateOnly date, int contextDays)
    {
        var priorDates = GetAvailableTickDates(symbol)
            .Where(d => d < date)
            .OrderByDescending(d => d)
            .Take(contextDays);

        var candles = new List<CandleData>();
        foreach (var d in priorDates) candles.AddRange(LoadUnderlyingCandlesForDay(symbol, d));
        candles.AddRange(LoadUnderlyingCandlesForDay(symbol, date));
        return candles.OrderBy(c => c.Time).ToList();
    }

    // Same as above but for the 1h panel specifically: HourlyCandleStore already has ~200 days of
    // real OHLC persisted (far better than our flat per-tick candles), so use that for every day
    // BEFORE the replayed one, and only fall back to flat tick-built candles for the target date
    // itself (which still needs to build up step by step as the simulator advances).
    public static List<CandleData> LoadHourlyCandlesWithContext(string symbol, DateOnly date)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var priorFromStore = HourlyCandleStore.Load(symbol)
            .Where(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.Time, eastern)) < date);

        return priorFromStore
            .Concat(LoadUnderlyingCandlesForDay(symbol, date))
            .OrderBy(c => c.Time)
            .ToList();
    }

    private static List<DateOnly> GetAvailableTickDates(string symbol)
    {
        var dates = new HashSet<DateOnly>();

        void Scan(string folder, string pattern)
        {
            var dir = Path.Combine(folder, symbol);
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, pattern))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split('_');
                var last  = parts[^1]; // {Symbol}_Ticks_{yyyyMMdd} / {Symbol}_L1Ticks_{yyyyMMdd}
                if (DateTime.TryParseExact(last, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    dates.Add(DateOnly.FromDateTime(d));
            }
        }

        Scan(TicksLevelOneFolder, $"{symbol}_L1Ticks_*.csv");
        Scan(TicksFolder, $"{symbol}_Ticks_*.csv");
        return dates.ToList();
    }

    private static List<(DateTime Time, decimal Price)> LoadUnderlyingTicks(string symbol, DateOnly date)
    {
        var dateStr = date.ToString("yyyyMMdd");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        var l1Path = Path.Combine(TicksLevelOneFolder, symbol, $"{symbol}_L1Ticks_{dateStr}.csv");
        var path   = File.Exists(l1Path) ? l1Path : Path.Combine(TicksFolder, symbol, $"{symbol}_Ticks_{dateStr}.csv");
        if (!File.Exists(path)) return new();

        var result = new List<(DateTime, decimal)>();
        var lines  = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var eastTime)) continue;
            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) continue;
            // A missing/mis-mapped field on the wire shows up as a 0 price — LevelOneTickStore
            // saves every tick regardless (by design, for later comparison), but the live chart
            // never plots these (SchwabStreamerClient only raises OnLevelOneTick when
            // lastPrice > 0). Skip them here too, or a single 0-to-real-price "candle" per
            // contaminated bucket blows out the whole chart's Y-axis.
            if (price <= 0) continue;

            // Stored as Eastern wall-clock (see TickPriceStore/LevelOneTickStore) — convert back
            // to real UTC so it lines up with CandleAggregation's own Eastern conversions.
            var utcTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastTime, DateTimeKind.Unspecified), eastern);
            result.Add((utcTime, price));
        }
        return RemoveOutlierTicks(result.OrderBy(t => t.Item1).ToList());
    }

    // Drops isolated bad prints — a single tick that jumps far from its neighbor and snaps right
    // back on the very next tick (e.g. a stale/bad quote during thin pre-market liquidity), which
    // otherwise shows up as a fake long wick in whichever bucket it lands in. Only fires when the
    // NEXT tick reverts close to where the PREVIOUS one was — a real, sustained move (even a fast
    // one) never gets touched, since the follow-up tick stays away from the pre-jump price too.
    private static List<(DateTime Time, decimal Price)> RemoveOutlierTicks(List<(DateTime Time, decimal Price)> ticks)
    {
        if (ticks.Count < 3) return ticks;

        const decimal outlierPct = 0.004m; // 0.4% — comfortably above normal tick-to-tick noise
        var result = new List<(DateTime, decimal)> { ticks[0] };

        for (int i = 1; i < ticks.Count - 1; i++)
        {
            var prev = ticks[i - 1].Price;
            var cur  = ticks[i].Price;
            var next = ticks[i + 1].Price;

            var jump    = Math.Abs(cur - prev);
            var reverts = Math.Abs(next - prev);

            if (prev > 0 && jump / prev >= outlierPct && reverts / prev < outlierPct)
                continue; // isolated spike that snaps back right after — drop it

            result.Add(ticks[i]);
        }

        result.Add(ticks[^1]);
        return result;
    }

    // Latest price at or before the given step time — a step's underlying price should reflect
    // "what the market was doing AT that snapshot", not a price from later.
    private static decimal? NearestPriceAt(List<(DateTime Time, decimal Price)> ticks, DateTime stepTimeEastern)
    {
        if (ticks.Count == 0) return null;
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var stepUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(stepTimeEastern, DateTimeKind.Unspecified), eastern);

        decimal? best = null;
        foreach (var (time, price) in ticks)
        {
            if (time > stepUtc) break;
            best = price;
        }
        return best ?? ticks[0].Price;
    }

    private static void ReadQuotesInto(string path, string symbol, OptionType type, SortedDictionary<TimeOnly, List<OptionQuoteDto>> byTime)
    {
        var lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            var p = lines[i].Split(',');
            if (p.Length < 12) continue;
            if (!TimeOnly.TryParse(p[0], out var time)) continue;

            var spot        = ParseDecimal(p[1]);
            var strike      = ParseDecimal(p[2]);
            var intrinsic   = ParseDecimal(p[5]);

            var quote = new OptionQuoteDto
            {
                Symbol             = symbol,
                OptionType         = type,
                SpotPrice          = spot,
                StrikePrice        = strike,
                Bid                = ParseDecimal(p[3]),
                Ask                = ParseDecimal(p[4]),
                IntrinsicValue     = intrinsic,
                ExtrinsicValue     = ParseDecimal(p[6]),
                Delta              = ParseDecimal(p[7]),
                Gamma              = ParseDecimal(p[8]),
                Theta              = ParseDecimal(p[9]),
                Vega               = ParseDecimal(p[10]),
                ImpliedVolatility  = ParseDecimal(p[11]),
                // CsvLogger doesn't log Schwab's own "inTheMoney" flag — derived from intrinsic
                // value instead (same idea: a real intrinsic value means the option is ITM).
                InTheMoney         = intrinsic > 0
            };

            if (!byTime.TryGetValue(time, out var list))
                byTime[time] = list = new List<OptionQuoteDto>();
            list.Add(quote);
        }
    }

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
