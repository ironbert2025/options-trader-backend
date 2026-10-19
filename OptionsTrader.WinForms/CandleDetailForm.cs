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

    private DateTime _bucketStartUtc;           // the 15-minute candle now being followed (the latest)
    private DateTime _firstBucketStartUtc;      // the first one this window was opened on — formed candles stay from here
    private Func<DateTime, DateTime, List<CandleData>>? _getMinutes;
    private Func<DateTime, MinuteBucketState>? _getState;
    private bool _freezeWhenClosed;
    private bool _following;
    private volatile bool _dirty;
    private string _lastJson = string.Empty;
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private bool _pendingViewReset = true;
    private bool _sawForming;                   // the candle on screen was seen forming: when it ends, follow the next one
    private double? _parentBarSpacing;
    private Func<DateTime>? _getClockUtc;       // "now" of the data (last tick / replay clock), for the seconds of the forming minute
    private MinuteBucketState _lastState = MinuteBucketState.Closed;
    private int _lastMinuteCount;
    private string _lastOverlay = string.Empty; // last header text sent into the chart
    private const double CandlesShareOfWidth = 0.8; // fallback only: 15 candles take this share of the plot width
    private const double PriceAxisWidthPx = 60;
    // The window is half as wide as it used to be (760 px), but the first candle stays where it was: 20 % of
    // the OLD plot width from the left edge, in pixels — now about 44 % of the narrower plot.
    private const int OldClientWidth = 760;
    private const int ClientWidth = 380;
    private const double FirstCandleXPx = 0.2 * (OldClientWidth - PriceAxisWidthPx);

    // panelHeight: the height of the 15m panel it came from, so both charts have the same vertical size.
    public CandleDetailForm(string symbol, int panelHeight)
    {
        _symbol = symbol;
        Text = $"{symbol} — velas de 1 min";
        ClientSize = new Size(ClientWidth, Math.Max(280, panelHeight + 30));
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
            else UpdateTitle(); // the seconds of the forming minute move even when no data changed
        };
        FormClosed += (s, e) => { _redrawTimer.Stop(); _redrawTimer.Dispose(); };
    }

    // Targets a 15-minute bucket. getMinutes(startUtc, endUtc) returns the 1-minute candles of the range;
    // getState says whether the bucket is still forming. freezeWhenClosed: stop following once it is over
    // (the live case); the Simulator keeps following because its clock can be rewound.
    // ---- Docking: the window sits against the right edge of its parent (see WindowDocking) ----
    private Form? _dockParent;
    private Func<int>? _dockTopAlign;
    private Point? _lastDocked;

    // First docking, before the window is shown: top aligned with topAlignScreenY() (the chart panel on screen),
    // and from then on it follows the parent while it stays where it was put.
    public void DockToRightOf(Form parent, Func<int> topAlignScreenY)
    {
        _dockParent = parent;
        _dockTopAlign = topAlignScreenY;
        parent.LocationChanged += OnParentMoved;
        parent.SizeChanged += OnParentMoved;
        FormClosed += (s, e) => { parent.LocationChanged -= OnParentMoved; parent.SizeChanged -= OnParentMoved; };
        Redock();
    }

    // Another double click: re-dock only if the user has not dragged the window somewhere else.
    public void DockIfStillDocked()
    {
        if (_dockParent != null && (_lastDocked == null || Location == _lastDocked)) Redock();
    }

    private void OnParentMoved(object? sender, EventArgs e) => DockIfStillDocked();

    private void Redock()
    {
        if (_dockParent == null || _dockParent.IsDisposed || _dockParent.WindowState == FormWindowState.Minimized) return;
        var area = Screen.FromControl(_dockParent).WorkingArea;
        var topY = _dockTopAlign?.Invoke() ?? _dockParent.Top;
        var target = WindowDocking.ComputeDockedLocation(_dockParent.Bounds, Size, area, topY);
        Location = target;
        _lastDocked = target;
    }

    public async Task ShowBucketAsync(DateTime bucketStartUtc, Func<DateTime, DateTime, List<CandleData>> getMinutes,
        Func<DateTime, MinuteBucketState> getState, bool freezeWhenClosed, double? priceMin, double? priceMax, double? parentBarSpacing, Func<DateTime> getClockUtc, string? linesJson)
    {
        _getClockUtc = getClockUtc;
        _bucketStartUtc = bucketStartUtc;
        _firstBucketStartUtc = bucketStartUtc;
        _getMinutes = getMinutes;
        _getState = getState;
        _freezeWhenClosed = freezeWhenClosed;
        _following = true;
        _sawForming = false;
        _parentBarSpacing = parentBarSpacing;
        _lastJson = string.Empty;

        await EnsurePageAsync();

        // New target: lay the view out once — a fixed bar width (15 candles take ~80 % of the plot), the
        // price axis over the same span the 15m chart shows. Redraws then leave the view alone.
        // Each 1-minute candle gets the SAME width in pixels as the 15-minute candle it belongs to (the
        // parent chart's bar width at its current zoom). Only without it (unknown) is a width derived from
        // this window's size.
        await ConfigureViewAsync(priceMin, priceMax);
        await _web.CoreWebView2.ExecuteScriptAsync($"detailSetLines({(string.IsNullOrWhiteSpace(linesJson) ? "[]" : linesJson)});");
        _pendingViewReset = true;

        await RedrawAsync();
        _redrawTimer.Start();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task ConfigureViewAsync(double? priceMin, double? priceMax)
    {
        var plotWidth = Math.Max(200, _web.Width - PriceAxisWidthPx);
        var barSpacing = _parentBarSpacing is > 0 ? _parentBarSpacing.Value : plotWidth * CandlesShareOfWidth / 15.0;
        var viewJson = JsonSerializer.Serialize(new { barSpacing, priceMin, priceMax, bucketStart = ChartPanel.FakeUtcEpochSeconds(_bucketStartUtc), firstCandlePx = FirstCandleXPx });
        await _web.CoreWebView2.ExecuteScriptAsync($"configureDetailView({viewJson});");
    }

    // The candle being followed has just ended: the window moves on to the next 15-minute candle (now forming);
    // the one it showed stays behind as a formed candle. Only inside the regular session (last candle 15:45).
    private async Task<bool> AdvanceToNextBucketAsync()
    {
        var next = _bucketStartUtc.AddMinutes(15);
        if (TimeZoneInfo.ConvertTimeFromUtc(next, EasternZone).TimeOfDay >= new TimeSpan(16, 0, 0)) return false;
        _bucketStartUtc = next;
        _sawForming = false;
        _lastJson = string.Empty;
        _lastOverlay = string.Empty;
        _following = true;
        // The candles already drawn stay (formed, to the left, a gray line marks the boundary); the view scrolls to the new one.
        await _web.CoreWebView2.ExecuteScriptAsync($"detailAdvanceBucket({ChartPanel.FakeUtcEpochSeconds(next)});");
        _pendingViewReset = false;
        return true;
    }

    // A trade opened or closed: its white rayita goes on the 1-minute candle of that moment (only drawn if
    // the moment falls inside the 15-minute candle this window shows).
    public async void AddTrade(decimal price, long eventSeconds, bool isClose)
    {
        if (IsDisposed || _web.CoreWebView2 == null || _pageReady == null || !_pageReady.Task.IsCompletedSuccessfully) return;
        try
        {
            var priceArg = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _web.CoreWebView2.ExecuteScriptAsync($"detailAddTrade({priceArg}, {eventSeconds}, {(isClose ? "true" : "false")});");
        }
        catch { /* diagnostics window */ }
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

    // Title: window, symbol, status — and, while the 15-minute candle is forming, which minute of the 15 is
    // being drawn and how many seconds of it have gone by (from the data's own clock, so in the Simulator it
    // follows the replay).
    private void UpdateTitle()
    {
        if (_getClockUtc == null || IsDisposed) return;
        var startEt = TimeZoneInfo.ConvertTimeFromUtc(_bucketStartUtc, EasternZone);
        var statusText = _lastState switch
        {
            MinuteBucketState.Forming => "en formación",
            MinuteBucketState.Settling => "cerrando…",
            _ => "cerrada"
        };
        var title = $"{_symbol} — {startEt:HH:mm}–{startEt.AddMinutes(15):HH:mm} · velas de 1 min · {_lastMinuteCount}/15 · {statusText}";
        if (_lastState == MinuteBucketState.Forming)
        {
            var clock = _getClockUtc();
            var minuteIndex = Math.Clamp((int)(clock - _bucketStartUtc).TotalMinutes + 1, 1, 15);
            title += $" · vela {minuteIndex} · {clock.Second:00} s";
        }
        if (Text != title) Text = title;

        // The same data inside the chart (bottom left, yellow), over two lines so it fits the narrow window.
        var overlay = $"{_symbol} — {startEt:HH:mm}–{startEt.AddMinutes(15):HH:mm} · velas de 1 min · {_lastMinuteCount}/15\n{statusText}";
        if (_lastState == MinuteBucketState.Forming)
        {
            var clock = _getClockUtc();
            var minuteIndex = Math.Clamp((int)(clock - _bucketStartUtc).TotalMinutes + 1, 1, 15);
            overlay += $" · vela {minuteIndex} · {clock.Second:00} s";
        }
        if (overlay != _lastOverlay && _web.CoreWebView2 != null && _pageReady?.Task.IsCompletedSuccessfully == true)
        {
            _lastOverlay = overlay;
            _ = _web.CoreWebView2.ExecuteScriptAsync($"detailSetHeaderText({JsonSerializer.Serialize(overlay)});");
        }
    }

    private async Task RedrawAsync()
    {
        if (_getMinutes == null || _getState == null || _web.CoreWebView2 == null || IsDisposed) return;
        _dirty = false;

        var endUtc = _bucketStartUtc.AddMinutes(15);
        var state = _getState(_bucketStartUtc);
        var minutes = _getMinutes(_firstBucketStartUtc, endUtc); // every candle followed so far, formed ones included

        var json = ChartPanel.ToChartJsonPublic(minutes);
        if (json != _lastJson)
        {
            _lastJson = json;
            await _web.CoreWebView2.ExecuteScriptAsync($"detailSetCandles({json}, {(_pendingViewReset ? "true" : "false")});");
            _pendingViewReset = false;
        }

        _lastState = state;
        _lastMinuteCount = minutes.Count(m => m.Time >= _bucketStartUtc); // minutes of the candle being followed
        UpdateTitle();

        if (state == MinuteBucketState.Forming) _sawForming = true;
        else if (state == MinuteBucketState.Closed && _sawForming)
        {
            if (await AdvanceToNextBucketAsync()) { await RedrawAsync(); return; }
        }

        // Over: one last draw above included the final data; stop following (the live case).
        if (_freezeWhenClosed && state == MinuteBucketState.Closed) _following = false;
    }
}
