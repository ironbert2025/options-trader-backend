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
    }
}
