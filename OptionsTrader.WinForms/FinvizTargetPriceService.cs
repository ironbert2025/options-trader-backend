using System.Net.Http;
using System.Text.RegularExpressions;

namespace OptionsTrader.WinForms;

// Scrapes the analyst "Target Price" consensus off Finviz's own quote page — Finviz has no public
// API for this, only what's rendered in the HTML (confirmed live via DevTools: label is an <a>
// inside a div.snapshot-td-label, value is the NEXT sibling div.snapshot-td2). Per explicit
// request, only pulled for individual stocks the user actually trades (AAPL, TSLA, NFLX, NVDA) —
// ETFs (SPY/QQQ/DIA/IWM) don't have analyst price targets at all, so those never even hit the
// network. This is scraping, not an official API — kept to a low-frequency refresh (see
// TwoPanelChartsControl's own timer) instead of the app's normal 6s polling cadence, and the whole
// thing is best-effort: a markup change on Finviz's end should silently return null, never throw
// into the caller.
internal static class FinvizTargetPriceService
{
    private static readonly HashSet<string> SupportedSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "AAPL", "TSLA", "NFLX", "NVDA"
    };

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static FinvizTargetPriceService()
    {
        // Finviz blocks/serves a different (JS-only) page to requests with no browser-like
        // User-Agent — confirmed needed when this was first tried without one.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
    }

    // "Target Price" label, then the next snapshot-td2 <td> (the value cell), then the first
    // number inside its <span>. Confirmed against Finviz's actual server-rendered HTML (NOT the
    // React-hydrated DOM devtools shows, which restructures this into nested divs) —
    // `...Target Price</a></div></td><td class="snapshot-td2 ..."><div class="snapshot-td-content">
    // <a ...><b><span class="color-text is-positive">333.14</span></b></a></div></td>...`.
    private static readonly Regex TargetPricePattern = new(
        @"Target Price</a></div></td>\s*<td class=""snapshot-td2[^""]*""[^>]*>.*?<span[^>]*>([\d]+(?:\.[\d]+)?)</span>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static async Task<decimal?> GetTargetPriceAsync(string symbol)
    {
        if (!SupportedSymbols.Contains(symbol)) return null;

        try
        {
            var html = await Http.GetStringAsync($"https://finviz.com/quote.ashx?t={symbol}");
            var match = TargetPricePattern.Match(html);
            if (!match.Success)
            {
                DebugLog($"symbol={symbol} htmlLen={html.Length} regex did NOT match (page fetched OK, markup likely different than expected)");
                return null;
            }
            if (!decimal.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                DebugLog($"symbol={symbol} regex matched '{match.Groups[1].Value}' but decimal.TryParse failed");
                return null;
            }
            DebugLog($"symbol={symbol} OK price={price}");
            return price;
        }
        catch (Exception ex)
        {
            // Best-effort — a network hiccup or a Finviz markup change must never bubble up into
            // the chart UI. Still logged (temporary diagnostic) so a silent failure can actually be
            // diagnosed instead of just "the label never shows up."
            DebugLog($"symbol={symbol} THREW: {ex}");
            return null;
        }
    }

    private static void DebugLog(string message)
    {
        try
        {
            const string path = @"C:\OptionsData\EventLog\finviz_targetprice_debug.log";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* best-effort diagnostic logging */ }
    }
}
