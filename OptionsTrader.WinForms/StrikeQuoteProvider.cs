using System.Globalization;
using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.WinForms;

internal sealed record StrikeQuote(decimal Strike, decimal Bid, decimal Ask, decimal Delta = 0m, decimal Gamma = 0m);

// What a host hands the 1-minute window for a double-clicked candle: the quotes of the 4 nearest OTM strikes (or none, with the
// reason in Note) as of the last second of that minute (or right now, for the minute still forming).
internal sealed record StrikeQuoteResult(IReadOnlyList<StrikeQuote> Quotes, string? Note = null, decimal Spot = 0m);

internal static class StrikeQuotePicker
{
    // The 4 strikes OTM nearest to the spot: calls = strikes at/above the spot, ascending; puts = at/below it, descending.
    // Only quotes with a live market (bid > 0 and ask > 0) take part.
    public static IReadOnlyList<StrikeQuote> Nearest(IEnumerable<(decimal Strike, decimal Bid, decimal Ask, decimal Delta, decimal Gamma)> quotes, bool calls, decimal spot, int count = 4)
    {
        var live = quotes.Where(q => q.Bid > 0 && q.Ask > 0).GroupBy(q => q.Strike).Select(g => g.First());
        var picked = calls
            ? live.Where(q => q.Strike >= spot).OrderBy(q => q.Strike)
            : live.Where(q => q.Strike <= spot).OrderByDescending(q => q.Strike);
        return picked.Take(count).Select(q => new StrikeQuote(q.Strike, q.Bid, q.Ask, q.Delta, q.Gamma)).ToList();
    }

    // EXACTLY these strikes (in the given order), whether they are OTM or ITM by now. A strike with no quote in the data is left out.
    public static IReadOnlyList<StrikeQuote> ForStrikes(IEnumerable<(decimal Strike, decimal Bid, decimal Ask, decimal Delta, decimal Gamma)> quotes, IReadOnlyList<decimal> strikes)
    {
        var byStrike = quotes.GroupBy(q => q.Strike).ToDictionary(g => g.Key, g => g.First());
        return strikes.Where(byStrike.ContainsKey).Select(s => new StrikeQuote(s, byStrike[s].Bid, byStrike[s].Ask, byStrike[s].Delta, byStrike[s].Gamma)).ToList();
    }

    public static IReadOnlyList<StrikeQuote> ForStrikes(IEnumerable<OptionQuoteDto> quotes, bool calls, IReadOnlyList<decimal> strikes) =>
        ForStrikes(quotes
            .Where(q => q.OptionType == (calls ? OptionsTrader.Domain.Enums.OptionType.Call : OptionsTrader.Domain.Enums.OptionType.Put))
            .Select(q => (q.StrikePrice, q.Bid, q.Ask, q.Delta, q.Gamma)), strikes);

    public static IReadOnlyList<StrikeQuote> Nearest(IEnumerable<OptionQuoteDto> quotes, bool calls, decimal spot, int count = 4) =>
        Nearest(quotes
            .Where(q => q.OptionType == (calls ? OptionsTrader.Domain.Enums.OptionType.Call : OptionsTrader.Domain.Enums.OptionType.Put))
            .Select(q => (q.StrikePrice, q.Bid, q.Ask, q.Delta, q.Gamma)), calls, spot, count);
}

// The chain as the polling saved it (C:\OptionsData\Trades\Iv\{SYM}_{Call|Put}_{day}_{exp}.csv): the last poll at or before the end of
// a given minute. Times in that file are the machine's local clock (CsvLogger writes DateTime.Now). Read-only, shares the file with
// the logger that may still be appending to it.
internal static class OptionChainHistory
{
    private const string Folder = @"C:\OptionsData\Trades\Iv";

    // strikes: when given, exactly those strikes (ITM or not); otherwise the 4 nearest OTM.
    public static StrikeQuoteResult GetAtEndOfMinute(string symbol, DateOnly expiration, bool calls, DateTime minuteStartUtc, IReadOnlyList<decimal>? strikes = null)
    {
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(minuteStartUtc, TimeZoneInfo.Local);
        var day = DateOnly.FromDateTime(startLocal);
        var path = Path.Combine(Folder, $"{symbol}_{(calls ? "Call" : "Put")}_{day:yyyyMMdd}_{expiration:yyyyMMdd}.csv");
        if (!File.Exists(path)) return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones guardados (CSV)");

        var cutoff = startLocal.TimeOfDay + TimeSpan.FromSeconds(59);
        var lowerBound = startLocal.TimeOfDay - TimeSpan.FromMinutes(2); // a poll older than this is not "that minute's" data
        TimeSpan? lastTime = null;
        var rows = new List<(decimal Strike, decimal Bid, decimal Ask, decimal Delta, decimal Gamma)>();
        decimal spot = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            reader.ReadLine(); // header
            while (reader.ReadLine() is { } line)
            {
                var p = line.Split(',');
                if (p.Length < 5 || !TimeSpan.TryParse(p[0], CultureInfo.InvariantCulture, out var t)) continue;
                if (t > cutoff) break;
                if (t < lowerBound) continue;
                if (lastTime != t) { lastTime = t; rows.Clear(); spot = 0; }
                if (decimal.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) spot = s;
                if (decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var k) &&
                    decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) &&
                    decimal.TryParse(p[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var ask))
                {
                    decimal delta = 0m, gamma = 0m;
                    if (p.Length > 8)
                    {
                        decimal.TryParse(p[7], NumberStyles.Any, CultureInfo.InvariantCulture, out delta);
                        decimal.TryParse(p[8], NumberStyles.Any, CultureInfo.InvariantCulture, out gamma);
                    }
                    rows.Add((k, bid, ask, delta, gamma));
                }
            }
        }
        catch (IOException) { return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones (archivo ocupado)"); }

        if (lastTime == null || spot <= 0) return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones en ese minuto");
        var quotes = strikes != null ? StrikeQuotePicker.ForStrikes(rows, strikes) : StrikeQuotePicker.Nearest(rows, calls, spot);
        return quotes.Count > 0 ? new StrikeQuoteResult(quotes, null, spot) : new StrikeQuoteResult(quotes, "sin datos de opciones en ese minuto", spot);
    }
}

// One strike's outcome if the spot reaches the target: the option price is estimated from the delta and gamma of the moment
// (bid + delta x dS + 1/2 gamma x dS^2, constant IV, theta ignored), the contracts come from the position budget, the net is
// after the broker commission. BreakEvenMove = how far the spot has to go (in its favor) just to cover the spread and the commission.
internal sealed record TargetEstimate(decimal Strike, decimal Ask, decimal ExitBid, int Contracts, decimal Net, decimal BreakEvenMove, bool Reaches2xBreakEven)
{
    public bool Best { get; init; }
}

internal static class TargetEstimator
{
    public const decimal CommissionPerContract = 1.30m;

    // dS = how far the spot has to move in favor of the leg: target - spot for calls, spot - target for puts (negative = against it).
    public static List<TargetEstimate> Compute(IReadOnlyList<StrikeQuote> quotes, decimal spot, decimal target, bool bullish, Func<decimal, int> contractsFor)
    {
        var dS = bullish ? target - spot : spot - target;
        var list = new List<TargetEstimate>();
        foreach (var q in quotes)
        {
            var delta = Math.Abs(q.Delta);
            var gamma = q.Gamma;
            var change = delta * dS + 0.5m * gamma * dS * dS;
            var exit = Math.Max(0m, q.Bid + change);
            var contracts = Math.Max(0, contractsFor(q.Ask));
            var perContract = 100m * (exit - q.Ask) - CommissionPerContract;
            var need = (q.Ask - q.Bid) + CommissionPerContract / 100m;
            var be = BreakEven(delta, gamma, need);
            list.Add(new TargetEstimate(q.Strike, q.Ask, exit, contracts, Math.Round(contracts * perContract, 2), be, dS >= 2m * be && be < 1000m));
        }
        // best = highest net among the strikes whose move covers at least twice their break-even move
        var best = list.Where(e => e.Reaches2xBreakEven && e.Net > 0).OrderByDescending(e => e.Net).FirstOrDefault();
        return best == null ? list : list.Select(e => e == best ? e with { Best = true } : e).ToList();
    }

    private static decimal BreakEven(decimal delta, decimal gamma, decimal need)
    {
        if (delta <= 0m && gamma <= 0m) return 1000m;
        if (gamma <= 0.000001m) return delta <= 0m ? 1000m : Math.Round(need / delta, 2);
        var a = (double)(0.5m * gamma); var b = (double)delta; var c = -(double)need;
        var x = (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return Math.Round((decimal)x, 2);
    }
}
