using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Standalone window showing up to the last 250 Daily candles for a symbol (enough for SMA100/200
// to actually have data, not just SMA20/40), with SMA20/40/100/200 and Bollinger Bands(20,2) — so
// the "PM" (Punto Medio / SMA20 slope) indicator on the 1h panel can be related back to the
// actual daily candles. A brand-new WebView2/page load, deliberately NOT a toggle on the live 1h
// panel's own chart: toggling Daily in-place there hit an unresolved rendering bug (correct data,
// correct axis range, but candles stayed invisible until a manual scroll/zoom) that survived every
// attempted fix (repaint tricks, a dedicated second series, even a real OS-level mouse scroll). A
// fresh page load doesn't carry over whatever state that bug depended on — same chart.html, same
// candlestick rendering the live panels already use without issue, just fed daily-bucketed
// candles instead of hourly ones, with no live streaming/toggle involved at all.
public class DailyChartForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly string _symbol;
    private readonly List<CandleData> _dailyCandles;

    public DailyChartForm(string symbol, List<CandleData> dailyCandles)
    {
        _symbol = symbol;
        _dailyCandles = dailyCandles;

        Text          = $"{symbol} — Daily";
        Width         = 900;
        Height        = 600;
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(_webView);
        Load += async (s, e) => await InitAsync();
    }

    private async Task InitAsync()
    {
        await _webView.EnsureCoreWebView2Async();

        var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
        var navDone = new TaskCompletionSource();
        _webView.CoreWebView2.NavigationCompleted += (s, args) =>
        {
            if (args.IsSuccess) navDone.TrySetResult();
        };

        // Same cache-busting query string ChartPanel.LoadHistoryAsync uses — forces a fresh read
        // of chart.html instead of a stale cached copy from an earlier window this session.
        var chartUri = new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}";
        _webView.CoreWebView2.Navigate(chartUri);
        await navDone.Task;

        await _webView.CoreWebView2.ExecuteScriptAsync("configureSmas([20,40,100,200]);");
        await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");

        // Each daily bar IS one "day" by construction, so "50 days" of visible window is exactly
        // the 50 bars passed in — same configureVisibleDays/applyVisibleRange machinery the live
        // panels already use, no separate logic needed here.
        await _webView.CoreWebView2.ExecuteScriptAsync($"configureVisibleDays({_dailyCandles.Count});");

        var json = ChartPanel.ToChartJsonPublic(_dailyCandles);
        await _webView.CoreWebView2.ExecuteScriptAsync($"loadHistory({json});");

        await EvaluatePisoTechoAsync();

        // Blue "current price" line, per explicit request — same primitive the 1h/15m RTH panels'
        // premarket line uses, just anchored on the Daily chart's still-forming "today" bar instead.
        // Armed here unconditionally; MultiChartForm feeds it the live spot via UpdateLivePrice as
        // ticks arrive (see OnLiveTick relay) — before the first tick lands, it just stays hidden.
        await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");
    }

    // Ported from ChartPanel.EvaluatePisoTechoPair/EvaluateSingleSmaPisoTecho — SAME criterion
    // panel 1 uses (fast/slow SMA pairs (20,40) and (100,200), bearish alignment -> that SMA is
    // "Techo" only while price stays below it, bullish -> "Piso" only while price stays above it),
    // just evaluated against the Daily series' last CLOSED bar instead of the 1h panel's own
    // _closedCandles — if the most recent daily candle is TODAY's (still forming, market open),
    // it's excluded so this reads exactly like panel 1's own pre-market snapshot, not a live value
    // that would flicker as today's still-forming daily bar moves.
    private async Task EvaluatePisoTechoAsync()
    {
        var closed = _dailyCandles;
        if (closed.Count > 0)
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var todayEastern = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern));
            var lastBarDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(closed[^1].Time, eastern));
            if (lastBarDate >= todayEastern) closed = closed[..^1];
        }

        (string? Fast, string? Slow) EvaluatePair(int fastPeriod, int slowPeriod)
        {
            var fast = Sma(closed, fastPeriod);
            var slow = Sma(closed, slowPeriod);
            if (fast == null || slow == null || fast == slow) return (null, null);

            var price = closed[^1].Close;
            var bearish = fast < slow;
            return (EvaluateSingleSmaPisoTecho(fast.Value, price, bearish), EvaluateSingleSmaPisoTecho(slow.Value, price, bearish));
        }

        var (r20, r40) = EvaluatePair(20, 40);
        var (r100, r200) = EvaluatePair(100, 200);

        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(20, {ToJsStringOrNull(r20)}, 40, {ToJsStringOrNull(r40)});");
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(100, {ToJsStringOrNull(r100)}, 200, {ToJsStringOrNull(r200)});");
    }

    private static decimal? Sma(List<CandleData> closed, int period)
    {
        if (closed.Count < period) return null;
        decimal sum = 0;
        for (int i = closed.Count - period; i < closed.Count; i++) sum += closed[i].Close;
        return sum / period;
    }

    private static string? EvaluateSingleSmaPisoTecho(decimal sma, decimal price, bool bearishAlignment) =>
        bearishAlignment ? (price < sma ? "Techo" : null) : (price > sma ? "Piso" : null);

    private static string ToJsStringOrNull(string? value) => value == null ? "null" : $"'{value}'";

    // Fed by MultiChartForm (hourlyPanel.OnLiveTick relay) — updates the blue "current price" line
    // to today's live spot, whether that's a premarket tick or a live RTH price. No-op once this
    // window is closed/disposed.
    public async Task UpdateLivePrice(decimal price)
    {
        if (IsDisposed || _webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"updatePreMarketLine({price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, null);");
    }
}
