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

    // ---- Click on a 1-minute candle: its 4 nearest OTM strikes (green candle -> calls, red -> puts) ----
    // The host sets StrikeProvider(minuteStartUtc, bullish, forming): the quotes as of the last second of that minute, or the
    // live ones for the minute still forming. Null (the Daily form) -> the click does nothing.
    // The 4th argument, when not null, asks for EXACTLY those strikes (ITM or not) instead of the 4 nearest OTM.
    public Func<DateTime, bool, bool, IReadOnlyList<decimal>?, StrikeQuoteResult?>? StrikeProvider { get; set; }

    // The price target (the Simulator's P-Line). With a target set, the leg (calls / puts) comes from it, not from the candle's color,
    // and each drawing gets an estimate of every strike if the spot reaches it. ContractsFor(ask): contracts the position budget buys.
    public Func<decimal, int>? ContractsFor { get; set; }
    private decimal? _targetPrice;
    private bool _targetBullish;
    // A fixed spot move in USD (the quick .50 / 1.00 buttons): used instead of the target price while set; the leg is then the candle's color.
    private decimal? _deltaSpot;
    // The P-Line the fixed move draws on its own (the price the spot must reach, from the clicked candle to the right) and the spot
    // each drawing was calculated at.
    private bool _moveLineActive;
    private decimal _moveLinePrice;        // the price that P-Line sits at = the target of BOTH estimate blocks while it is on
    private bool _moveLineFromRight;       // which drawing the 2nd click created (the reference)
    // Once the live spot reaches the target line (the move line or a hand-placed P-Line) the estimates stop being recalculated: the blocks
    // keep the values of that moment while the spot keeps moving. A new click / line / target starts over.
    private bool _targetCrossed;
    private DateTime _targetCrossedAtUtc;

    private bool SpotCrossedTarget(decimal spot)
    {
        if (spot <= 0) return false;
        if (_moveLineActive)
        {
            var bullish = (_moveLineFromRight ? _formingPick?.Bullish : _formedPick?.Bullish) ?? true;
            return bullish ? spot >= _moveLinePrice : spot <= _moveLinePrice;
        }
        return _targetPrice is { } target && (_targetBullish ? spot >= target : spot <= target);
    }
    private decimal _leftSpot, _rightSpot;

    // The PnL% path of the right drawing's strikes from the 2nd click (the moment the line appears) to the target: the entry is the ask
    // of the reference drawing at that moment; the exit is the live bid (real market, not the model), less the commission. Only the
    // worst and the best % reached are kept; they freeze when the spot crosses the target.
    private Dictionary<decimal, decimal>? _pnlEntry;
    private readonly Dictionary<decimal, (decimal Min, decimal Max)> _pnlRange = new();

    private void StartPnl(bool fromRight)
    {
        _pnlRange.Clear();
        _pnlEntry = null;
        if (!_estCache.TryGetValue(fromRight ? "forming" : "formed", out var c) || c.Quotes.Count == 0) return;
        _pnlEntry = c.Quotes.Where(q => q.Ask > 0).ToDictionary(q => q.Strike, q => q.Ask);
    }

    private void StopPnl() { _pnlEntry = null; _pnlRange.Clear(); }

    private static decimal PnlPercent(decimal entryAsk, decimal bid) =>
        (100m * (bid - entryAsk) - TargetEstimator.CommissionPerContract) / (100m * entryAsk) * 100m;
    // The last quotes of each drawing, so the estimate blocks can be rebuilt without asking the provider again.
    private readonly Dictionary<string, (IReadOnlyList<StrikeQuote> Quotes, decimal Spot, string Label, bool Bullish)> _estCache = new();

    // With a fixed move on and both drawings present: a P-Line at spot ± ΔS (above for a CALL, below for a PUT) from the candle just
    // clicked. It replaces a hand-placed P-Line (only one exists) and is only a marker: the estimate keeps using the fixed move.
    private async Task DrawMoveLineAsync(bool right)
    {
        if (_deltaSpot is not { } dS || _formedPick == null || _formingPick == null || _web.CoreWebView2 == null) return;
        var spot = right ? _rightSpot : _leftSpot;
        if (spot <= 0) return;
        var bullish = right ? _formingPick.Value.Bullish : _formedPick.Value.Bullish;
        var minute = right ? _formingPick.Value.Minute : _formedPick.Value.Minute;
        var price = spot + (bullish ? dS : -dS);
        _targetPrice = null;
        _moveLineActive = true;
        _moveLinePrice = price;
        _moveLineFromRight = right;
        _targetCrossed = false; // a new line: the calculation starts again
        StartPnl(right);
        RebuildEstimateBlocks(); // the calculations now reference the 2nd click: the target is this line's price for BOTH drawings
        await PushEstimatesAsync();
        await _web.CoreWebView2.ExecuteScriptAsync(
            $"detailDrawMovePLine({ChartPanel.FakeUtcEpochSeconds(minute)}, {price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {(bullish ? "true" : "false")});");
        UpdateAnalysisStatus();
    }

    private async Task ClearMoveLineAsync()
    {
        if (!_moveLineActive) return;
        _moveLineActive = false;
        _targetCrossed = false;
        StopPnl();
        if (_web.CoreWebView2 != null && _pageReady?.Task.IsCompletedSuccessfully == true)
        {
            await _web.CoreWebView2.ExecuteScriptAsync("detailClearMovePLine();");
            RebuildEstimateBlocks(); // back to each drawing's own ΔS
            await PushEstimatesAsync();
        }
    }

    // The quick buttons: set (or clear with null) the fixed move and recalculate. With no drawing yet, the candle forming gets one
    // right away (its leg from its color), so pressing the button already shows the numbers.
    public async void SetDeltaSpot(decimal? move)
    {
        _deltaSpot = move;
        try
        {
            if (!move.HasValue) await ClearMoveLineAsync();
            await RefreshPickAsync(right: false);
            await RefreshPickAsync(right: true);
            if (move.HasValue && _moveLineActive) await DrawMoveLineAsync(_moveLineFromRight); // .50 <-> 1.00: the line moves to the new distance
            else if (move.HasValue && _formedPick != null && _formingPick != null) await DrawMoveLineAsync(right: true); // armed again with both drawings on
            await PushEstimatesAsync();
        }
        catch { /* diagnostics window */ }
        UpdateAnalysisStatus();
    }
    private readonly Dictionary<string, object> _estimateBlocks = new(); // page slot -> block of the estimate box

    // The target changed (a P-Line placed or deleted in this window): the picks follow its leg and get re-estimated.
    private async Task ApplyTargetAsync(decimal? price, bool bullish)
    {
        _targetCrossed = false; // another target: the calculation starts again
        StopPnl();
        _targetPrice = price;
        _targetBullish = bullish;
        try
        {
            if (price.HasValue && _deltaSpot == null)
            {
                if (_formedPick is { } l) _formedPick = (l.Minute, bullish);
                if (_formingPick is { } r) _formingPick = (r.Minute, bullish, r.Live);
            }
            await RefreshPickAsync(right: false);
            await RefreshPickAsync(right: true);
            if (price.HasValue && _formingPick != null) StartPnl(fromRight: true); // hand-placed P-Line: the path starts from the right drawing
            await PushEstimatesAsync();
        }
        catch { /* diagnostics window */ }
        UpdateAnalysisStatus();
    }

    // ---- The analysis bar (Simulator only): P-Line + the quick .50 / 1.00 moves, in a strip above the chart ----
    private Panel? _analysisBar;
    private Button? _btnPLine, _btnMove05, _btnMove10;
    private Label? _lblAnalysis;
    private const int AnalysisBarHeight = 30;

    private void BuildAnalysisBar()
    {
        _analysisBar = new Panel { Dock = DockStyle.Top, Height = AnalysisBarHeight, BackColor = Color.FromArgb(19, 23, 34), Visible = false };
        Button MakeButton(string text, int x, int w)
        {
            var b = new Button { Text = text, Location = new Point(x, 3), Size = new Size(w, 24), FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = Color.FromArgb(40, 46, 62), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Color.DimGray;
            _analysisBar.Controls.Add(b);
            return b;
        }
        _btnPLine = MakeButton("P-Line", 4, 62);
        _btnMove05 = MakeButton(".50", 70, 46);
        _btnMove10 = MakeButton("1.00", 120, 46);
        _lblAnalysis = new Label { AutoSize = true, Location = new Point(174, 8), ForeColor = Color.Silver, Font = new Font("Segoe UI", 8F), Text = "Sin objetivo" };
        _analysisBar.Controls.Add(_lblAnalysis);

        // P-Line: one price line on THIS chart = the price target. Above the last price -> CALL, below -> PUT.
        _btnPLine.Click += async (s, e) =>
        {
            if (_web.CoreWebView2 == null || _pageReady?.Task.IsCompletedSuccessfully != true) return;
            var on = await _web.CoreWebView2.ExecuteScriptAsync("togglePLine();") == "true";
            SetButtonLit(_btnPLine, on);
        };
        // .50 / 1.00: a fixed spot move; they toggle and replace each other; the calculation is immediate.
        _btnMove05.Click += (s, e) => PressMove(0.50m);
        _btnMove10.Click += (s, e) => PressMove(1.00m);
    }

    private void SetButtonLit(Button? b, bool on)
    {
        if (b == null) return;
        b.BackColor = on ? Color.MediumPurple : Color.FromArgb(40, 46, 62);
    }

    private void SetMoveButtons(decimal? move)
    {
        SetButtonLit(_btnMove05, move == 0.50m);
        SetButtonLit(_btnMove10, move == 1.00m);
    }

    private void PressMove(decimal value)
    {
        var next = _deltaSpot == value ? (decimal?)null : value;
        SetMoveButtons(next);
        SetDeltaSpot(next);
        UpdateAnalysisStatus();
    }

    private void UpdateAnalysisStatus()
    {
        if (_lblAnalysis == null || IsDisposed) return;
        _lblAnalysis.Text = _deltaSpot is { } move ? $"ΔS fijo +{move:0.00}"
            : _targetPrice is { } target ? $"Objetivo {target:F2} · {(_targetBullish ? "CALL" : "PUT")}"
            : "Sin objetivo";
    }

    // Called by the Simulator when it opens this window: shows the bar (the window grows to hold it) and gives the position sizing
    // the estimate needs (contracts the budget buys at a given ask).
    public void EnableAnalysisBar(Func<decimal, int> contractsFor)
    {
        ContractsFor = contractsFor;
        if (_analysisBar == null || _analysisBar.Visible) return;
        _analysisBar.Visible = true;
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + AnalysisBarHeight);
        _deltaSpot = DefaultMove; // .50 is armed from the start: nothing is calculated until the 2nd click
        SetMoveButtons(DefaultMove);
        UpdateAnalysisStatus();
    }

    private const decimal DefaultMove = 0.50m;

    // A new day was loaded: forget the target, the fixed move and what was drawn from them.
    public async void ResetAnalysis()
    {
        _targetPrice = null;
        _targetCrossed = false;
        StopPnl();
        _deltaSpot = _analysisBar?.Visible == true ? DefaultMove : (decimal?)null; // a new day: back to the default, .50 armed
        SetMoveButtons(_deltaSpot);
        SetButtonLit(_btnPLine, false);
        try
        {
            if (_web.CoreWebView2 != null && _pageReady?.Task.IsCompletedSuccessfully == true)
                await _web.CoreWebView2.ExecuteScriptAsync("clearPLines();");
            _moveLineActive = false;
            await ClearPickAsync();
        }
        catch { /* diagnostics window */ }
        UpdateAnalysisStatus();
    }

    // The strikes the LEFT drawing shows: the RIGHT one repeats them (same side, same strikes) so the same options can be compared.
    private IReadOnlyList<decimal>? _leftStrikes;
    // Two independent drawings that coexist, at the LEFT and RIGHT edge: the first formed candle clicked goes LEFT; the RIGHT one is the
    // live drawing of the candle still forming (follows the newest forming minute) or, when a second formed candle is clicked, that
    // candle (it takes the place of the live one). With both holding formed candles, a new click replaces the older. Neither goes away
    // by itself: a click on the same candle toggles it off. (Page slots: "formed" = left, "forming" = right.)
    private (DateTime Minute, bool Bullish)? _formedPick;                  // LEFT
    private (DateTime Minute, bool Bullish, bool Live)? _formingPick;      // RIGHT (Live = follows the forming candle)
    private DateTime _formedSetAt = DateTime.MinValue, _formingSetAt = DateTime.MinValue;
    private DateTime _lastPickRefreshUtc = DateTime.MinValue;
    private bool _pickBusy;
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
        BuildAnalysisBar();
        Controls.Add(_web);
        Controls.Add(_analysisBar!);   // below the top bar, above the chart (hidden until the Simulator enables it)
        Controls.Add(topBar);

        _redrawTimer.Tick += async (s, e) =>
        {
            if (_dirty && _following) await RedrawAsync();
            else UpdateTitle(); // the seconds of the forming minute move even when no data changed
            // the strikes of the minute still forming follow the live quotes (about once a second); once that minute is over they stay as they were
            // The forming drawing FOLLOWS the newest forming candle: every second it takes the live chain and the minute now forming,
            // minute after minute (and into the next 15-minute candle), until it is deleted or toggled off. It only stops while nothing is forming.
            if (_formingPick is { Live: true } && _lastState == MinuteBucketState.Forming && (DateTime.UtcNow - _lastPickRefreshUtc).TotalSeconds >= 1)
                await RefreshPickAsync(right: true);
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
        await ClearPickAsync(); // a new target: the strikes picked on the previous one go away

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

    private async void OnPageMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var msgType = root.TryGetProperty("type", out var msgTypeEl) ? msgTypeEl.GetString() : null;
            if (msgType == "pline_add")
            {
                // The P-Line placed in this window = the price target: above the last price a CALL, below a PUT. It replaces a fixed move.
                if (root.TryGetProperty("price", out var plPrice) && plPrice.TryGetDecimal(out var targetPrice))
                {
                    var bullish = !(root.TryGetProperty("direction", out var plDir) && plDir.GetString() == "below");
                    if (_deltaSpot != null) { _deltaSpot = null; SetMoveButtons(null); }
                    _moveLineActive = false; // the page's single P-Line is now the hand-placed one
                    await ApplyTargetAsync(targetPrice, bullish);
                }
                return;
            }
            if (msgType == "pline_delete") { _moveLineActive = false; await ApplyTargetAsync(null, false); return; }
            if (msgType == "pline_placed") { SetButtonLit(_btnPLine, false); return; }
            if (root.TryGetProperty("type", out var delType) && delType.GetString() == "detail_strikes_deleted")
            {
                // Delete on a selected drawing: the page already removed it — forget its pick so a new double click starts fresh.
                var slot = root.TryGetProperty("slot", out var slotEl) ? slotEl.GetString() : null;
                if (slot == "forming") { _formingPick = null; _estimateBlocks.Remove("forming"); _estCache.Remove("forming"); }
                else if (slot == "formed") { _formedPick = null; _leftStrikes = null; _estimateBlocks.Remove("formed"); _estCache.Remove("formed"); }
                await ClearMoveLineAsync(); // the line needs both drawings
                await PushEstimatesAsync();
                return;
            }
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "detail_minute_click") return;
            if (StrikeProvider == null || !root.TryGetProperty("time", out var tEl) || !tEl.TryGetInt64(out var seconds)) return;
            var minute = ChartPanel.UtcFromFakeEpochSeconds(seconds);
            var forming = IsMinuteForming(minute);
            _targetCrossed = false; // a new selection: the calculation starts again
            StopPnl();
            bool? up = root.TryGetProperty("up", out var upEl) && (upEl.ValueKind == JsonValueKind.True || upEl.ValueKind == JsonValueKind.False)
                ? upEl.GetBoolean() : (bool?)null; // null = doji
            if (_targetPrice.HasValue && _deltaSpot == null) up = _targetBullish; // with a price target the leg is the target's, whatever the candle's color

            if (forming)
            {
                // The candle still forming: its live drawing takes the RIGHT edge (replacing a second formed candle that was there);
                // a click on it while it is live turns it off.
                if (_formingPick is { Live: true }) { await ClearPickAsync("forming"); return; }
                if (up == null) return;
                _formingPick = (minute, up.Value, true);
                _formingSetAt = DateTime.UtcNow;
                await RefreshPickAsync(right: true);
                await DrawMoveLineAsync(right: true);
                return;
            }

            // A candle that had already closed: the same candle again turns its drawing off.
            if (_formedPick is { } left && left.Minute == minute) { await ClearPickAsync("formed"); return; }
            if (_formingPick is { Live: false } right && right.Minute == minute) { await ClearPickAsync("forming"); return; }
            if (up == null) return; // a doji has no direction: nothing to draw

            // Where it goes: LEFT if free; otherwise RIGHT (over the live drawing or when free); with both holding formed candles, over the older.
            bool toLeft;
            if (_formedPick == null) toLeft = true;
            else if (_formingPick == null || _formingPick.Value.Live) toLeft = false;
            else toLeft = _formedSetAt <= _formingSetAt;
            if (toLeft)
            {
                _formedPick = (minute, up.Value);
                _formedSetAt = DateTime.UtcNow;
                await RefreshPickAsync(right: false);
                if (_formingPick != null) await RefreshPickAsync(right: true); // the right drawing repeats the strikes of this new left one
                await DrawMoveLineAsync(right: false);
            }
            else
            {
                _formingPick = (minute, up.Value, false);
                _formingSetAt = DateTime.UtcNow;
                await RefreshPickAsync(right: true);
                await DrawMoveLineAsync(right: true);
            }
        }
        catch { /* diagnostics window */ }
    }

    // Is this minute the one still being formed (the last candle of a candle that is still forming)?
    private bool IsMinuteForming(DateTime minute) =>
        _lastState == MinuteBucketState.Forming && _getClockUtc != null && MinuteCandleStore.MinuteStart(_getClockUtc()) == minute;

    // right: the drawing at the RIGHT edge (page slot "forming"); otherwise the LEFT one (page slot "formed").
    private async Task RefreshPickAsync(bool right)
    {
        (DateTime Minute, bool Bullish, bool Live)? pickOrNull = right
            ? _formingPick
            : (_formedPick is { } l ? (l.Minute, l.Bullish, false) : null);
        if (pickOrNull is not { } pick || StrikeProvider == null || _pickBusy || _web.CoreWebView2 == null) return;
        _pickBusy = true;
        try
        {
            if (right) _lastPickRefreshUtc = DateTime.UtcNow;
            // The live drawing asks for the live chain and always targets the minute now forming (the side — calls or puts — stays the
            // one chosen at the click); with nothing forming it keeps its last values. A formed candle's drawing asks for its last second.
            var live = right && pick.Live && _lastState == MinuteBucketState.Forming && _getClockUtc != null;
            if (live)
            {
                pick = (MinuteCandleStore.MinuteStart(_getClockUtc!()), pick.Bullish, true);
                _formingPick = pick;
            }
            // With a LEFT drawing on, the RIGHT one shows the same strikes and the same side as the LEFT one.
            IReadOnlyList<decimal>? anchor = right && _formedPick != null && _leftStrikes is { Count: > 0 } ? _leftStrikes : null;
            if (anchor != null) pick = (pick.Minute, _formedPick!.Value.Bullish, pick.Live);
            var result = StrikeProvider(pick.Minute, pick.Bullish, live, anchor);
            var quotes = result?.Quotes ?? Array.Empty<StrikeQuote>();
            if (result != null) { if (right) _rightSpot = result.Spot; else _leftSpot = result.Spot; }
            if (!right) _leftStrikes = quotes.Count > 0 ? quotes.Select(q => q.Strike).ToList() : null;
            // PnL% path (right drawing only, while a target line exists): worst / best % reached so far; frozen once the target is crossed.
            if (right && _pnlEntry != null && !_targetCrossed)
                foreach (var q in quotes)
                    if (_pnlEntry.TryGetValue(q.Strike, out var entryAsk) && q.Bid > 0)
                    {
                        var p = PnlPercent(entryAsk, q.Bid);
                        _pnlRange[q.Strike] = _pnlRange.TryGetValue(q.Strike, out var r) ? (Math.Min(r.Min, p), Math.Max(r.Max, p)) : (p, p);
                    }
            var payload = new
            {
                slot = right ? "forming" : "formed",
                minute = ChartPanel.FakeUtcEpochSeconds(pick.Minute),
                bullish = pick.Bullish,
                label = TimeZoneInfo.ConvertTimeFromUtc(pick.Minute, EasternZone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
                note = quotes.Count == 0 ? (result?.Note ?? "sin datos de opciones") : null,
                lines = quotes.Select(q => new
                {
                    strike = q.Strike, bid = q.Bid, ask = q.Ask,
                    pnlMin = right && _pnlEntry != null && _pnlRange.TryGetValue(q.Strike, out var pr) ? (decimal?)Math.Min(pr.Min, 0m) : null,
                    pnlMax = right && _pnlEntry != null && _pnlRange.TryGetValue(q.Strike, out var pr2) ? (decimal?)Math.Max(pr2.Max, 0m) : null
                })
            };
            await _web.CoreWebView2.ExecuteScriptAsync($"detailSetStrikes({JsonSerializer.Serialize(payload)});");

            // The estimate box is rebuilt from what each drawing last showed.
            var slotKey = right ? "forming" : "formed";
            if (!_targetCrossed) _estCache[slotKey] = (quotes, result?.Spot ?? 0m, (string)payload.label, pick.Bullish);
            if (live && !_targetCrossed && SpotCrossedTarget(result?.Spot ?? 0m))
            {
                _targetCrossed = true; // this refresh is the last one that calculates
                _targetCrossedAtUtc = _getClockUtc?.Invoke() ?? DateTime.UtcNow;
            }
            RebuildEstimateBlocks();
            await PushEstimatesAsync();
        }
        catch { /* diagnostics window */ }
        finally { _pickBusy = false; }
    }

    // One block per drawing. The move each is calculated with:
    //  - fixed move (.50 / 1.00): ONLY the drawing of the 2nd click (when the move line is shown) is calculated, with ΔS from its own
    //    spot; the 1st click calculates nothing;
    //  - a hand-placed P-Line: distance from each drawing's spot to its price.
    private void RebuildEstimateBlocks()
    {
        _estimateBlocks.Clear();
        if (ContractsFor == null) return;
        foreach (var (slotKey, c) in _estCache)
        {
            if (c.Quotes.Count == 0 || c.Spot <= 0) continue;
            if (slotKey == "formed" ? _formedPick == null : _formingPick == null) continue;
            decimal? move = _deltaSpot is { } fixedMove
                ? (_moveLineActive && slotKey == (_moveLineFromRight ? "forming" : "formed") ? fixedMove : (decimal?)null)
                : (_targetPrice is { } tp ? TargetEstimator.MoveToTarget(c.Spot, tp, c.Bullish) : null);
            if (move is not { } dS) continue;
            var estimates = TargetEstimator.Compute(c.Quotes, dS, ContractsFor);
            _estimateBlocks[slotKey] = new
            {
                label = $"{c.Label} · {(c.Bullish ? "CALL" : "PUT")} · spot {c.Spot:F2} · ΔS {dS:+0.00;-0.00}" +
                        (_targetCrossed ? $" · ✔ objetivo alcanzado {TimeZoneInfo.ConvertTimeFromUtc(_targetCrossedAtUtc, EasternZone).ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty),
                rows = estimates.Select(e => new
                {
                    strike = e.Strike.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    exitText = e.ExitBid.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    contracts = e.Contracts,
                    netText = (e.Net >= 0 ? "+$" : "-$") + Math.Abs(e.Net).ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                    beText = "+" + e.BreakEvenMove.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    best = e.Best,
                    ok = e.Reaches2xBreakEven
                }).ToList()
            };
        }
    }

    // The estimate box: header with the target and the budget, then the left drawing's block and the right one's.
    private async Task PushEstimatesAsync()
    {
        if (_web.CoreWebView2 == null || _pageReady?.Task.IsCompletedSuccessfully != true) return;
        if ((_targetPrice == null && _deltaSpot == null) || _estimateBlocks.Count == 0)
        {
            await _web.CoreWebView2.ExecuteScriptAsync("detailSetEstimates(null);");
            return;
        }
        var blocks = new List<object>();
        if (_estimateBlocks.TryGetValue("formed", out var left) && _formedPick != null) blocks.Add(left);
        if (_estimateBlocks.TryGetValue("forming", out var right) && _formingPick != null) blocks.Add(right);
        var headerLead = _deltaSpot is { } fixedMove
            ? $"ΔS fijo +{fixedMove:0.00} · objetivo {_moveLinePrice:F2} (2.º click)"
            : $"Objetivo {_targetPrice!.Value:F2} · {(_targetBullish ? "CALL" : "PUT")}";
        var payload = new { header = $"{headerLead} · neto tras comisión 1.30/contr.", blocks };
        await _web.CoreWebView2.ExecuteScriptAsync($"detailSetEstimates({JsonSerializer.Serialize(payload)});");
    }

    // slot: "formed" | "forming" | null (both).
    private async Task ClearPickAsync(string? slot = null)
    {
        if (slot != "forming") { _formedPick = null; _leftStrikes = null; _estimateBlocks.Remove("formed"); _estCache.Remove("formed"); }
        if (slot != "formed") { _formingPick = null; _estimateBlocks.Remove("forming"); _estCache.Remove("forming"); }
        await ClearMoveLineAsync(); // the line needs both drawings
        if (_web.CoreWebView2 != null && _pageReady?.Task.IsCompletedSuccessfully == true)
            await _web.CoreWebView2.ExecuteScriptAsync(slot == null ? "detailClearStrikes();" : $"detailClearStrikes('{slot}');");
        await PushEstimatesAsync();
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
            _web.CoreWebView2.WebMessageReceived += OnPageMessage;
            _web.CoreWebView2.NavigationCompleted += (s, args) => { if (args.IsSuccess) _pageReady.TrySetResult(); };
            _web.CoreWebView2.Navigate(new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}");
            await _pageReady.Task;
            await _web.CoreWebView2.ExecuteScriptAsync("setPLineSingle(true);"); // one P-Line at a time: the price target
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
