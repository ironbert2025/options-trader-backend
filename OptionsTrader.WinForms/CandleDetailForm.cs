using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Window that shows the 1-minute candles inside ONE 15-minute candle (opened by a double click on a
// candle of the 15m RTH chart, live Charts tab or Simulator). A candle already formed shows its 15
// minutes; the one still forming is followed live — the window redraws as the minutes move — and stops
// plotting once that 15-minute candle is over. One window per symbol: another double click re-targets it.
internal sealed class CandleDetailForm : Form
{
    private readonly string _symbol;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _redrawTimer = new() { Interval = 300 };
    private TaskCompletionSource? _pageReady;

    private DateTime _bucketStartUtc;
    private Func<DateTime, DateTime, List<CandleData>>? _getMinutes;
    private Func<DateTime, MinuteBucketState>? _getState;
    private bool _freezeWhenClosed;
    private bool _following;
    private volatile bool _dirty;
    private string _lastJson = string.Empty;
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private bool _pendingViewReset = true;
    private const double CandlesShareOfWidth = 0.8; // 15 candles take this share of the plot width at the initial zoom
    private const double PriceAxisWidthPx = 60;

    // panelHeight: the height of the 15m panel it came from, so both charts have the same vertical size.
    public CandleDetailForm(string symbol, int panelHeight)
    {
        _symbol = symbol;
        Text = $"{symbol} — velas de 1 min";
        ClientSize = new Size(760, Math.Max(280, panelHeight + 30));
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(19, 23, 34);

        var resetButton = new Button
        {
            Text = "Restablecer vista", Dock = DockStyle.Left, Width = 130, FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White, BackColor = Color.FromArgb(40, 46, 62), UseVisualStyleBackColor = false
        };
        resetButton.FlatAppearance.BorderColor = Color.DimGray;
        resetButton.Click += async (s, e) =>
        {
            if (_web.CoreWebView2 != null) await _web.CoreWebView2.ExecuteScriptAsync("detailResetView();");
        };
        var topBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.FromArgb(19, 23, 34) };
        topBar.Controls.Add(resetButton);
        Controls.Add(_web);
        Controls.Add(topBar);

        _redrawTimer.Tick += async (s, e) =>
        {
            if (_dirty && _following) await RedrawAsync();
        };
        FormClosed += (s, e) => { _redrawTimer.Stop(); _redrawTimer.Dispose(); };
    }

    // Targets a 15-minute bucket. getMinutes(startUtc, endUtc) returns the 1-minute candles of the range;
    // getState says whether the bucket is still forming. freezeWhenClosed: stop following once it is over
    // (the live case); the Simulator keeps following because its clock can be rewound.
    public async Task ShowBucketAsync(DateTime bucketStartUtc, Func<DateTime, DateTime, List<CandleData>> getMinutes,
        Func<DateTime, MinuteBucketState> getState, bool freezeWhenClosed, double? priceMin, double? priceMax)
    {
        _bucketStartUtc = bucketStartUtc;
        _getMinutes = getMinutes;
        _getState = getState;
        _freezeWhenClosed = freezeWhenClosed;
        _following = true;
        _lastJson = string.Empty;

        await EnsurePageAsync();

        // New target: lay the view out once — a fixed bar width (15 candles take ~80 % of the plot), the
        // price axis over the same span the 15m chart shows. Redraws then leave the view alone.
        var plotWidth = Math.Max(200, _web.Width - PriceAxisWidthPx);
        var barSpacing = plotWidth * CandlesShareOfWidth / 15.0;
        var viewJson = JsonSerializer.Serialize(new { barSpacing, priceMin, priceMax });
        await _web.CoreWebView2.ExecuteScriptAsync($"configureDetailView({viewJson});");
        _pendingViewReset = true;

        await RedrawAsync();
        _redrawTimer.Start();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // The data behind the target changed (a tick, a bar, the Simulator clock) — redrawn on the next timer tick.
    public void MarkDirty() => _dirty = true;

    private async Task EnsurePageAsync()
    {
        if (_pageReady != null) { await _pageReady.Task; return; }
        _pageReady = new TaskCompletionSource();
        try
        {
            await _web.EnsureCoreWebView2Async();
            var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
            _web.CoreWebView2.NavigationCompleted += (s, args) => { if (args.IsSuccess) _pageReady.TrySetResult(); };
            _web.CoreWebView2.Navigate(new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}");
            await _pageReady.Task;
        }
        catch (Exception ex)
        {
            _pageReady.TrySetException(ex);
            throw;
        }
    }

    private async Task RedrawAsync()
    {
        if (_getMinutes == null || _getState == null || _web.CoreWebView2 == null || IsDisposed) return;
        _dirty = false;

        var endUtc = _bucketStartUtc.AddMinutes(15);
        var state = _getState(_bucketStartUtc);
        var minutes = _getMinutes(_bucketStartUtc, endUtc);

        var json = ChartPanel.ToChartJsonPublic(minutes);
        if (json != _lastJson)
        {
            _lastJson = json;
            await _web.CoreWebView2.ExecuteScriptAsync($"detailSetCandles({json}, {(_pendingViewReset ? "true" : "false")});");
            _pendingViewReset = false;
        }

        var startEt = TimeZoneInfo.ConvertTimeFromUtc(_bucketStartUtc, EasternZone);
        var statusText = state switch
        {
            MinuteBucketState.Forming => "en formación",
            MinuteBucketState.Settling => "cerrando…",
            _ => "cerrada"
        };
        Text = $"{_symbol} — {startEt:HH:mm}–{startEt.AddMinutes(15):HH:mm} · velas de 1 min · {minutes.Count}/15 · {statusText}";

        // Over: one last draw above included the final data; stop following (the live case).
        if (_freezeWhenClosed && state == MinuteBucketState.Closed) _following = false;
    }
}
