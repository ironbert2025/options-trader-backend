using System.Globalization;
using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.WinForms;

internal sealed record StrikeQuote(decimal Strike, decimal Bid, decimal Ask);

// What a host hands the 1-minute window for a double-clicked candle: the quotes of the 4 nearest OTM strikes (or none, with the
// reason in Note) as of the last second of that minute (or right now, for the minute still forming).
internal sealed record StrikeQuoteResult(IReadOnlyList<StrikeQuote> Quotes, string? Note = null);

internal static class StrikeQuotePicker
{
    // The 4 strikes OTM nearest to the spot: calls = strikes at/above the spot, ascending; puts = at/below it, descending.
    // Only quotes with a live market (bid > 0 and ask > 0) take part.
    public static IReadOnlyList<StrikeQuote> Nearest(IEnumerable<(decimal Strike, decimal Bid, decimal Ask)> quotes, bool calls, decimal spot, int count = 4)
    {
        var live = quotes.Where(q => q.Bid > 0 && q.Ask > 0).GroupBy(q => q.Strike).Select(g => g.First());
        var picked = calls
            ? live.Where(q => q.Strike >= spot).OrderBy(q => q.Strike)
            : live.Where(q => q.Strike <= spot).OrderByDescending(q => q.Strike);
        return picked.Take(count).Select(q => new StrikeQuote(q.Strike, q.Bid, q.Ask)).ToList();
    }

    public static IReadOnlyList<StrikeQuote> Nearest(IEnumerable<OptionQuoteDto> quotes, bool calls, decimal spot, int count = 4) =>
        Nearest(quotes
            .Where(q => q.OptionType == (calls ? OptionsTrader.Domain.Enums.OptionType.Call : OptionsTrader.Domain.Enums.OptionType.Put))
            .Select(q => (q.StrikePrice, q.Bid, q.Ask)), calls, spot, count);
}

// The chain as the polling saved it (C:\OptionsData\Trades\Iv\{SYM}_{Call|Put}_{day}_{exp}.csv): the last poll at or before the end of
// a given minute. Times in that file are the machine's local clock (CsvLogger writes DateTime.Now). Read-only, shares the file with
// the logger that may still be appending to it.
internal static class OptionChainHistory
{
    private const string Folder = @"C:\OptionsData\Trades\Iv";

    public static StrikeQuoteResult GetAtEndOfMinute(string symbol, DateOnly expiration, bool calls, DateTime minuteStartUtc)
    {
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(minuteStartUtc, TimeZoneInfo.Local);
        var day = DateOnly.FromDateTime(startLocal);
        var path = Path.Combine(Folder, $"{symbol}_{(calls ? "Call" : "Put")}_{day:yyyyMMdd}_{expiration:yyyyMMdd}.csv");
        if (!File.Exists(path)) return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones guardados (CSV)");

        var cutoff = startLocal.TimeOfDay + TimeSpan.FromSeconds(59);
        var lowerBound = startLocal.TimeOfDay - TimeSpan.FromMinutes(2); // a poll older than this is not "that minute's" data
        TimeSpan? lastTime = null;
        var rows = new List<(decimal Strike, decimal Bid, decimal Ask)>();
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
                    rows.Add((k, bid, ask));
            }
        }
        catch (IOException) { return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones (archivo ocupado)"); }

        if (lastTime == null || spot <= 0) return new StrikeQuoteResult(Array.Empty<StrikeQuote>(), "sin datos de opciones en ese minuto");
        var quotes = StrikeQuotePicker.Nearest(rows, calls, spot);
        return quotes.Count > 0 ? new StrikeQuoteResult(quotes) : new StrikeQuoteResult(quotes, "sin datos de opciones en ese minuto");
    }
}
