using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;
using System.Linq;

namespace OptionsTrader.WinForms;

// Panel 1 (1h) + panel 2 (15m RTH) — their ChartPanel instances, their own toolbars, and all
// event wiring that only ever involves those two panels — extracted out of MultiChartForm into a
// real UserControl so it can be embedded natively (correct keyboard routing, e.g. Delete on a
// selected T-Line) either inside MultiChartForm (alongside its own panel 3) or directly on
// Form1's "Charts" tab (no panel 3, no Form-in-Form embedding hack).
//
// Also owns the mirrored options grid ("Hoy"/"Próxima" tabs, click-to-trade) and mirrored trades
// grid (Close-button forwarding) — these aren't panel-1/2-specific in what they display (they
// mirror Form1's OWN quotes/trades, same as panel 3's own screenshot features), but Form1's
// Charts tab only ever constructs ONE TwoPanelChartsControl and never a MultiChartForm, so they
// live here too rather than being duplicated in both places. MultiChartForm's popup window still
// shows them exactly as before, now via this control instead of its own inline construction.
//
// A handful of toolbar controls that live in THIS control's own toolbar but also need to affect
// MultiChartForm's panel 3 (the shared ATH checkbox, H-Line button, and Text button/textbox — see
// MultiChartForm's own constructor) are exposed as public properties/events so MultiChartForm can
// attach an ADDITIONAL handler for its own panel 3, on top of this control's internal handling of
// panel 1/2 — see AthCheckBox/HLineButton/TextButton/ChartTextTextBox/OnTextPlaced below.
public class TwoPanelChartsControl : UserControl
{
    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly string _symbol;
    private readonly Form1 _form1;

    // Live options grid mirrored from Form1's own quotes (see Form1.OnQuotesUpdatedEvent /
    // GetQuoteSnapshot) — clicking Strike forwards into Form1.TriggerQuoteStrikeClick, which runs
    // the exact same click handler Form1's own grid would (same currently-selected trade mode).
    private readonly DataGridView _dgvOptions = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        ReadOnly = true,
        Font = new Font("Segoe UI", 8F),
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 24,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    // Same grid, mirroring Form1's NEXT-expiration chain (dgvQuotesNext) — shown in its own tab
    // (Fase 2 of the tabbed options-grid feature), only when Form1.IsNextExpDateVisible is true.
    private readonly DataGridView _dgvOptionsNext = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        ReadOnly = true,
        Font = new Font("Segoe UI", 8F),
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 24,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    // Trades grid mirrored from Form1's own dgvTrades (see Form1.OnTradesUpdatedEvent /
    // GetTradesGrid) — full read-only copy of values + per-cell colors every refresh; Close button
    // forwards into Form1.TriggerTradeCloseClick, same handler Form1's own grid uses.
    private readonly DataGridView _dgvTrades = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        Font = new Font("Segoe UI", 8F),
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 24,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    // Independent from Form1's own 4-way "Trade" radios (Options Quotes tab) — a strike clicked
    // here always opens WITH target, per explicit request. Always starts on Demo+Target on connect
    // (not persisted), separate instance per TwoPanelChartsControl (only one ever exists at a time).
    private readonly RadioButton _rbChartsDemoTarget = new() { Text = "Demo-Target", Checked = true, AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
    private readonly RadioButton _rbChartsRealTarget  = new() { Text = "Real-Target", AutoSize = true, ForeColor = Color.Green, Font = new Font("Segoe UI", 8F) };
    private bool _useRealTrade;

    private ChartPanel? _hourlyPanel;
    private ChartPanel? _rthPanel;

    public ChartPanel? HourlyPanel => _hourlyPanel;
    public ChartPanel? RthPanel => _rthPanel;

    // So Form1 can check this control is actually showing the trade's own symbol before feeding
    // it a strike/entry-spot marker — see MarkEntrySpotOnRthChartAsync below.
    public string Symbol => _symbol;

    // Set by MultiChartForm right after constructing this control, when it hosts it alongside its
    // own panel 3 — that window sends its OWN Telegram pushes / event-log screenshots (with the
    // full 3-panel image: Piso/Techo, T-Line Signal, SMA Cross, PM Cross) so this control's own
    // 2-panel-only versions (below) must stay silent there to avoid sending each event twice. Left
    // false (pushes enabled) when this control runs standalone on Form1's Charts tab, per explicit
    // request that the popup keep working exactly as it did before this control existed.
    public bool SuppressOwnTelegramPushes { get; set; }

    // White entry-spot line above the candle when a trade opens/closes — panel 2 (15m RTH) only,
    // per explicit request that the Charts tab's own panel 2 show it too (previously this only
    // ever reached the popup Live Chart window's rthPanel, via MultiChartForm.
    // MarkEntrySpotOnOvernightChartAsync — the Charts tab has no equivalent since it's a whole
    // separate MultiChartForm-less control Form1 never fed this into). Same underlying primitive/
    // persistence (OpenTradesStore) as the popup, just reached through this control directly.
    public async Task MarkEntrySpotOnRthChartAsync(decimal price)
    {
        if (_rthPanel != null) await _rthPanel.MarkEntrySpotAsync(price);
    }

    // Green "Stk=xxx" line at trade open — panel 2 (15m RTH) only, same pattern as
    // MarkEntrySpotOnRthChartAsync above.
    public async Task MarkStrikeOnRthChartAsync(decimal strike)
    {
        if (_rthPanel != null) await _rthPanel.MarkStrikeAsync(strike);
    }

    // "ΔS=value" label at trade close — panel 2 (15m RTH) only, same pattern as
    // MarkEntrySpotOnRthChartAsync above.
    public async Task MarkDeltaSOnRthChartAsync(decimal entrySpot, decimal closeSpot, decimal strike)
    {
        if (_rthPanel != null) await _rthPanel.MarkDeltaSAsync(entrySpot, closeSpot, strike);
    }

    // Small event log fed by panel 1/2 events — MultiChartForm's own panel-3/combined-screenshot
    // events also write into this SAME textbox (via AppendLog below) so the popup window still
    // shows one unified log, exactly like before the extraction.
    private readonly TextBox _crossLog;

    // Every "Daily" window currently open for this symbol — see MultiChartForm's original comment
    // (unchanged): removed on FormClosed so a closed window's WebView2 never gets touched again.
    private readonly List<DailyChartForm> _openDailyCharts = new();

    // Shared H-Line/Text toolbar controls (panel 2's toolbar) — public so MultiChartForm can wire
    // an additional handler that also toggles panel 3, matching original combined behavior.
    public CheckBox AthCheckBox { get; }
    public Button HLineButton { get; }
    public Button TextButton { get; }
    public TextBox ChartTextTextBox { get; }

    // Diagonal "Arrow" tool (line + arrowhead, red/green by drag direction) — per explicit
    // request, arms panel 1 AND panel 2 together (this control's own click handler below); public
    // so MultiChartForm can attach an additional handler onto the same button to also arm panel 3,
    // same split-button convention as HLineButton/TextButton above.
    public Button ArrowButton { get; }

    // Fired after this control's own T-Line/H-Line-style "Text" tool disarms itself on whichever
    // of panel 1/2 didn't place the text — MultiChartForm subscribes to also disarm panel 3.
    public event Action<ChartPanel?>? OnTextPlaced;

    public TwoPanelChartsControl(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, Form1 form1)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _form1         = form1;

        Dock = DockStyle.Fill;

        // Toolbar strip on top — 2-column layout (one group of controls per panel, each a single
        // compact row), matching the panels below so each group sits directly above its own chart.
        // Per explicit request: Rect/↑Verde/↓Roja/Daily/Día/ATH above panel 1, H-Line/T-Line/Text/
        // Arrow/BB edges above panel 2 (T-Line still arms BOTH panels when clicked — it just sits
        // visually in the panel 2 group), "Traer todas" removed entirely.
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 64,
            ColumnCount = 2,
            RowCount    = 2,
            Padding     = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

        var toolbarLeft = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoScroll    = false,
            Padding       = new Padding(4, 1, 4, 0)
        };
        var toolbarRight = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoScroll    = false,
            Padding       = new Padding(4, 1, 4, 0)
        };
        // AWS/Telegram (panel 2 group) didn't fit alongside H-Line/T-Line/Text/Arrow/BB edges in
        // toolbarRight's half-width column — same overflow/clipping issue the SMA Watch buttons had
        // earlier. Given their own second row, aligned under panel 2's own column only (panel 1's
        // row 1 cell stays empty — nothing was added there).
        var toolbarRightRow2 = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoScroll    = false,
            Padding       = new Padding(4, 1, 4, 0)
        };
        toolbar.Controls.Add(toolbarLeft, 0, 0);
        toolbar.Controls.Add(toolbarRight, 1, 0);
        toolbar.Controls.Add(toolbarRightRow2, 1, 1);

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            Padding     = new Padding(6, 2, 6, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ChartPanel? hourlyPanel = null;
        ChartPanel? rthPanel = null;
        var modes = new[] { ChartPanelMode.Hourly15, ChartPanelMode.Fifteen_RTH };
        for (int i = 0; i < modes.Length; i++)
        {
            var panel = new ChartPanel(symbol, _historyClient, _liveFeed, modes[i])
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6, 2, 6, 6)
            };
            layout.Controls.Add(panel, i, 0);
            if (modes[i] == ChartPanelMode.Hourly15) hourlyPanel = panel;
            if (modes[i] == ChartPanelMode.Fifteen_RTH) rthPanel = panel;
        }
        _hourlyPanel = hourlyPanel;
        _rthPanel    = rthPanel;

        // Stk-line/H-Line/ATH mirroring BETWEEN panel 1 and panel 2 — lives here (not just in
        // MultiChartForm) because this control is also used standalone (Form1's "Charts" tab has
        // no MultiChartForm/panel 3 at all) — confirmed live: an H-Line drawn on panel 1 there
        // never reached panel 2, since that mirroring used to be wired ONLY as part of
        // MultiChartForm's own 3-way (panel1/2/3) loop, which simply never runs outside the popup
        // window. MultiChartForm adds panel 3's OWN edges to this pair on top when it hosts this
        // control (see its constructor) — this 2-panel edge must never be duplicated there too, or
        // a draw/delete would double-fire across the pair.
        var twoPanels = new[] { hourlyPanel, rthPanel };
        foreach (var panel in twoPanels)
        {
            if (panel == null) continue;
            panel.OnStrikeDeletedEvent += price =>
            {
                foreach (var sibling in twoPanels)
                    if (sibling != null && sibling != panel) _ = sibling.RemoveStrikeLineAsync(price);
            };
            panel.OnHLineDeletedEvent += price =>
            {
                foreach (var sibling in twoPanels)
                    if (sibling != null && sibling != panel) _ = sibling.RemoveHLineAsync(price);
            };
            panel.OnHLineDrawnEvent += (time, price) =>
            {
                foreach (var sibling in twoPanels)
                    if (sibling != null && sibling != panel) _ = sibling.AddMirroredHLineAsync(time, price);
            };
        }
        if (hourlyPanel != null)
        {
            hourlyPanel.OnAllTimeHighUpdatedEvent += newValue =>
            {
                if (rthPanel != null) _ = rthPanel.MarkAllTimeHighAsync(newValue);
            };
        }

        // Piso/Techo Cruce/Rebote (panel 1) — arms panel 2's "Abriendo la Volatilidad" watch,
        // writes to this control's own crossLog, pushes the combined snapshot to Telegram, and
        // mirrors the reference line/daily PM onto panel 2. Lives here (not just in MultiChartForm)
        // for the same standalone reason as the mirroring mesh above — confirmed live: opening the
        // Charts tab without a popup Live Chart window meant these events had zero subscribers, so
        // panel 2's volatility watch never armed and nothing reached crossLog/Telegram there.
        // MultiChartForm still subscribes to the SAME hourlyPanel events (via HourlyPanel) to also
        // mirror onto its own panel 3 (overnightPanel) — this control's handlers below only ever
        // touch panel 1/2, so no double crossLog/Telegram firing when MultiChartForm hosts this.
        if (hourlyPanel != null && rthPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                var bullish = pisoTecho == "Techo" ? evento == "Cruce" : evento == "Rebote";
                rthPanel.ArmVolatilityOpeningWatch(bullish); // plain state/event, no CoreWebView2 — safe off the UI thread

                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendLog($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}");
                    if (!SuppressOwnTelegramPushes) _ = SendPisoTechoTelegramPushAsync(caption);
                });
            };

            hourlyPanel.OnPisoTechoLevelReadyEvent += (period, price) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    var sessionEnd   = GetTodaySessionEndFakeEpoch();
                    _ = rthPanel.MarkPisoTechoRefLineAsync(period, price, sessionStart, sessionEnd);
                });
            };

            hourlyPanel.OnDailyPmValueEvent += price =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    var visible = Form1.IsDailyPmLineEnabledFor(_symbol);
                    _ = hourlyPanel.MarkDailyPmLineAsync(price, sessionStart);
                    _ = rthPanel.MarkDailyPmLineAsync(price, sessionStart);
                    // Mark always updates the line's value even while hidden, so it's already
                    // current the moment "D.PM" gets re-checked — only the draw itself is gated.
                    _ = hourlyPanel.SetDailyPmLineVisibleAsync(visible);
                    _ = rthPanel.SetDailyPmLineVisibleAsync(visible);
                });
            };

            // Daily SMA40/100/200 lines — same idea as D.PM above but tab-Charts-only (panel 1/2),
            // per explicit request; MultiChartForm/panel 3 never subscribes to this event.
            hourlyPanel.OnDailySmaLineValueEvent += (period, price) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    var visible = Form1.GetDailySmaLinesEnabledFor(_symbol).Contains(period);
                    _ = hourlyPanel.MarkDailySmaLineAsync(period, price, sessionStart);
                    _ = rthPanel.MarkDailySmaLineAsync(period, price, sessionStart);
                    _ = hourlyPanel.SetDailySmaLineVisibleAsync(period, visible);
                    _ = rthPanel.SetDailySmaLineVisibleAsync(period, visible);
                });
            };

            hourlyPanel.OnPisoTechoLevelRemovedEvent += period =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => _ = rthPanel.RemovePisoTechoRefLineAsync(period));
            };

            // Race fix: hourlyPanel's HandleCreated can fire EvaluatePisoTechoOnce — and this very
            // event — before the subscriptions above ever run, especially when its history loads
            // fast (e.g. plenty of HourlyCandleStore data already cached locally). Catch up
            // immediately in that case.
            hourlyPanel.ReplayPisoTechoLevels();

            // T-Line + SMA20 breakout (panel 1) — pushes the combined snapshot, same as a trade
            // close. Same standalone reasoning as the Piso/Techo wiring above: only worked in the
            // popup before, since only MultiChartForm subscribed to it.
            hourlyPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    if (!SuppressOwnTelegramPushes) _ = SendTLineSignalTelegramPushAsync(message, "Hora");
                });
            };

            // SMA cross watch (Daily) — armed from DailyChartForm's "SMA Watch" buttons. Event log
            // already appended inside ChartPanel.EvaluateSmaCrossWatches itself; this just handles
            // crossLog + the Telegram push.
            hourlyPanel.OnSmaCrossEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    if (!SuppressOwnTelegramPushes) _ = SendSmaCrossTelegramPushAsync(message);
                });
            };

            // Daily-candle bounce off SMA20 — purely informational, log only (no Telegram). Safe to
            // leave unconditional (no image/Telegram involved), so no SuppressOwnTelegramPushes
            // check needed — the popup no longer double-subscribes to this one.
            hourlyPanel.OnDailyBounceEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        if (rthPanel != null)
        {
            // Panel 2's own T-Lines are independent from panel 1's — same breakout signal,
            // evaluated against panel 2's own SMA20/candles, logged/pushed identically.
            rthPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    if (!SuppressOwnTelegramPushes) _ = SendTLineSignalTelegramPushAsync(message, "15Min");
                });
            };

            // "Cruce de vela con PM" — log-only into the per-symbol events .md with a combined
            // snapshot, never crossLog, never Telegram (per explicit request).
            rthPanel.OnPmCrossEvent += caption =>
            {
                if (IsDisposed || SuppressOwnTelegramPushes) return;
                BeginInvoke(async () =>
                {
                    string? path = null;
                    try
                    {
                        using var combined = await CaptureCombinedChartImageAsync();
                        if (combined != null)
                        {
                            var folder = @"C:\OptionsTraderPush";
                            Directory.CreateDirectory(folder);
                            path = Path.Combine(folder, $"{_symbol}_PMCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    catch
                    {
                        path = null; // best-effort — the event still gets logged below without an image
                    }

                    EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
                });
            };
        }

        // Drawing tools + toggles for BOTH panels, consolidated into a single row above the charts
        // (previously split into a 2-column layout, one per panel, each with its own Clear button).
        // T-Line now arms BOTH panels at once with one button, same convention already used for
        // H-Line/Text/Arrow below — per explicit request. Both per-panel Clear buttons are gone
        // entirely: deleting a drawing is via the Delete key on a selected item now, not a bulk
        // clear (ClearDrawingsAsync is no longer called from here).
        var btnTLine = new Button { Text = "T-Line", Size = new Size(60, 24) };
        btnTLine.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleTLineModeAsync();
            if (rthPanel != null) on = await rthPanel.ToggleTLineModeAsync();
            btnTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        // Completing a T-Line (2nd click) auto-disarms itself in chart.html — reset the button
        // color here so it doesn't stay highlighted after the fact.
        if (hourlyPanel != null) hourlyPanel.OnTLinePlacedEvent += () => btnTLine.BackColor = SystemColors.Control;
        if (rthPanel != null) rthPanel.OnTLinePlacedEvent += () => btnTLine.BackColor = SystemColors.Control;

        // Filled gray rectangle marking sideways/consolidation ranges around price+SMAs (panel 1
        // only, unchanged). Click its border to select (yellow outline), then press Delete to
        // remove just that one.
        var btnRectGris = new Button { Text = "Rect", Size = new Size(60, 24) };
        btnRectGris.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleRectGrisModeAsync();
            btnRectGris.BackColor = on ? Color.LightGray : SystemColors.Control;
        };
        // chart.html auto-disarms this tool itself once the 2nd click completes a rectangle — per
        // explicit request, reset the button color to match. Same pattern as the sky-blue btnRect.
        if (hourlyPanel != null) hourlyPanel.OnRectGrisPlacedEvent += () => btnRectGris.BackColor = SystemColors.Control;

        // Single-click vertical arrows (panel 1 only, unchanged): green points up, red points down,
        // tip at the click point. Click the shaft to select (yellow dashed overlay), Delete removes it.
        var btnFlechaVerde = new Button { Text = "↑ Verde", Size = new Size(60, 24) };
        var btnFlechaRoja = new Button { Text = "↓ Roja", Size = new Size(60, 24) };
        btnFlechaVerde.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleFlechaVerdeModeAsync();
            btnFlechaVerde.BackColor = on ? Color.LightGreen : SystemColors.Control;
        };
        btnFlechaRoja.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleFlechaRojaModeAsync();
            btnFlechaRoja.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };
        // Placing one arrow (either color) auto-disarms itself in chart.html — reset the
        // corresponding button's color here so it doesn't stay highlighted after the fact.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnArrowPlacedEvent += up =>
            {
                if (up) btnFlechaVerde.BackColor = SystemColors.Control;
                else btnFlechaRoja.BackColor = SystemColors.Control;
            };
        }

        // Toggles the 1h panel between Daily (last 20 days, aggregated from up to ~200 trading
        // days of persisted hourly history) and plain Hourly candles.
        var btnDaily = new Button { Text = "Daily", Size = new Size(70, 24) };
        // Opens a separate window with its own fresh WebView2 instead of toggling in place on
        // this panel's own chart — see DailyChartForm's own comment for why (an unresolved
        // rendering bug in the in-place toggle: correct data/axis range, but candles stayed
        // invisible until a manual scroll).
        btnDaily.Click += (s, e) =>
        {
            var dailyCandles = ChartPanel.GetLastDailyCandles(_symbol, 250); // enough for SMA100/200 to have data
            var dailyForm = new DailyChartForm(_symbol, dailyCandles, _historyClient);
            _openDailyCharts.Add(dailyForm);
            dailyForm.FormClosed += (s2, e2) => _openDailyCharts.Remove(dailyForm);
            AttachDailyMirroring(dailyForm);
            dailyForm.Show();
        };

        // Dashed vertical lines separating the 7 hourly candles of each trading day (last 4 lines
        // = last 5 days; today's candles sit unbounded to the right of the most recent one).
        var chkDayDividers = new CheckBox { Text = "Día", AutoSize = true, Checked = true, Margin = new Padding(6, 4, 3, 3) };
        chkDayDividers.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ToggleDayDividersAsync();
        };

        // Shows/hides the ATH reference line — drawn on all 3 panels (panel 3 lives on
        // MultiChartForm, which attaches its own additional CheckedChanged handler onto this same
        // checkbox — see MultiChartForm's constructor).
        AthCheckBox = new CheckBox { Text = "ATH", AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 3) };
        AthCheckBox.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel != null) await hourlyPanel.SetAllTimeHighVisibleAsync(AthCheckBox.Checked);
            if (rthPanel != null) await rthPanel.SetAllTimeHighVisibleAsync(AthCheckBox.Checked);
        };

        // Single "Text" button arms text-placement mode on all panels at once — no mirroring, each
        // panel only places text where IT was clicked. Reads the Windows clipboard fresh each time
        // this button is pressed. Source text for the "Text" tool below — declared here (used by
        // TextButton.Click) but only actually added to the layout further down.
        ChartTextTextBox = new TextBox
        {
            Dock       = DockStyle.Top,
            Height     = 28,
            Multiline  = true,
            ScrollBars = ScrollBars.Vertical,
            Font       = new Font("Segoe UI", 9F)
        };
        // Enter on the last line stamps it with the current time (e.g. "9:34 Lo que escribí"),
        // copies that stamped line to the clipboard, and starts a fresh empty line for the next
        // note — per explicit request. Shift+Enter still inserts a plain newline (multi-line notes
        // before stamping). Only ever operates on the LAST line, since that's where typing/Enter
        // naturally happens; earlier lines are left alone.
        ChartTextTextBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Shift) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            var lines = ChartTextTextBox.Text.Split('\n');
            var lastIndex = lines.Length - 1;
            var lastLine = lines[lastIndex].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(lastLine)) return; // nothing typed on this line yet

            var stamped = $"{DateTime.Now:H:mm} {lastLine}";
            lines[lastIndex] = stamped;

            ChartTextTextBox.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            Clipboard.SetText(stamped);
            ChartTextTextBox.SelectionStart = ChartTextTextBox.Text.Length;
        };

        HLineButton = new Button { Text = "H-Line", Size = new Size(60, 24) };
        TextButton = new Button { Text = "Text", Size = new Size(60, 24) };
        ArrowButton = new Button { Text = "Arrow", Size = new Size(60, 24) };

        // Panel-1/2 half of the shared H-Line arm/disarm — MultiChartForm attaches its own extra
        // Click handler onto this same button to also toggle panel 3 (see its constructor), same
        // "sequential toggle → last result wins the button color" behavior as before the split.
        HLineButton.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleHLineModeAsync();
            if (rthPanel != null) on = await rthPanel.ToggleHLineModeAsync();
            HLineButton.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };
        // Panel-1/2 half of the shared Text arm/disarm — see TextButton's XML-ish comment above and
        // MultiChartForm's own extra Click handler for the panel-3 half.
        TextButton.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleTextModeAsync(ChartTextTextBox.Text);
            if (rthPanel != null) on = await rthPanel.ToggleTextModeAsync(ChartTextTextBox.Text);
            TextButton.BackColor = on ? Color.LightBlue : SystemColors.Control;
        };

        // A click on whichever panel actually placed the text auto-disarms just THAT panel's own
        // JS state (see chart.html's textArmed handling) — the other panels armed it too (the Text
        // tool arms all 3 at once) and are still waiting for a click of their own. Force the other
        // ONE of panel 1/2 off too and reset the button, then notify MultiChartForm (via
        // OnTextPlaced) so it can do the same for panel 3.
        void DisarmTextModeInternal(ChartPanel? placedOn)
        {
            TextButton.BackColor = SystemColors.Control;
            if (hourlyPanel != null && hourlyPanel != placedOn) _ = hourlyPanel.ToggleTextModeAsync(string.Empty);
            if (rthPanel != null && rthPanel != placedOn) _ = rthPanel.ToggleTextModeAsync(string.Empty);
            OnTextPlaced?.Invoke(placedOn);
        }
        if (hourlyPanel != null) hourlyPanel.OnTextPlacedEvent += () => DisarmTextModeInternal(hourlyPanel);
        if (rthPanel != null) rthPanel.OnTextPlacedEvent += () => DisarmTextModeInternal(rthPanel);
        _disarmTextModeInternal = DisarmTextModeInternal;

        // Panel-1/2 half of the shared diagonal Arrow arm/disarm — same "stays armed until pressed
        // again" toggle as DZ/SZ (no auto-disarm-on-placement, so no reset wiring needed beyond the
        // toggle's own return value). MultiChartForm attaches an extra Click handler onto this same
        // button to also arm panel 3, same split-button convention as HLineButton/TextButton.
        ArrowButton.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleArrowModeAsync();
            if (rthPanel != null) on = await rthPanel.ToggleArrowModeAsync();
            ArrowButton.BackColor = on ? Color.LightYellow : SystemColors.Control;
        };

        // Shows/hides the white Bollinger-band edge markers on this panel — checked by default
        // (matches the always-on behavior before this toggle existed).
        var chkBollingerEdges = new CheckBox { Text = "BB edges", AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 3) };
        chkBollingerEdges.CheckedChanged += async (s, e) =>
        {
            if (rthPanel == null) return;
            await rthPanel.SetBollingerEdgeMarkersVisibleAsync(chkBollingerEdges.Checked);
        };

        // "SMA Watch" buttons removed from this toolbar entirely, per explicit request — arming is
        // Daily-chart-only now (SmaDailyWatchStore + DailyChartForm's own buttons). The SMA the
        // watch evaluates is the DAILY-timeframe SMA (see ChartPanel.EvaluateSmaCrossWatches), which
        // is a different value than this panel's own hourly SMA line — having a button/marker here
        // implied it was watching the hourly SMA, which was misleading. Arming from Daily still
        // relays into the live 1h panel's detection (AttachDailyMirroring below), unchanged.

        // AWS/Telegram per-ticker toggles — same controls/persistence as the popup Live Chart's own
        // chkAws/chkTelegramEvents (MultiChartForm), just reachable here too, per explicit request.
        // AWS checked: trade opens/closes POST to the API and upload their Entry/Close screenshots
        // to S3; unchecked, both stay local-only (SaveTradeToApiAsync/UploadScreenshotAsync already
        // treat "AWS off" the same way they treat an unreachable API — see Form1.cs). Telegram
        // checked: the events this app is programmed to push (Piso/Techo, T-Line, Demand/Supply
        // Zone, auto-push, etc.) actually get sent; unchecked, none of them do — trade open/close
        // pushes are a separate, unrelated toggle-free path. Both read/write the SAME per-symbol
        // store (tickers.json) the popup uses, so toggling here affects that ticker everywhere.
        var chkAws = new CheckBox
        {
            Text     = "AWS",
            AutoSize = true,
            Checked  = Form1.IsAwsEnabledFor(_symbol),
            Margin   = new Padding(12, 4, 3, 3)
        };
        chkAws.CheckedChanged += (s, e) => Form1.SetAwsEnabledFor(_symbol, chkAws.Checked);

        var chkTelegram = new CheckBox
        {
            Text     = "Telegram",
            AutoSize = true,
            Checked  = Form1.IsTelegramEnabledFor(_symbol),
            Margin   = new Padding(3, 4, 3, 3)
        };
        chkTelegram.CheckedChanged += (s, e) => Form1.SetTelegramEnabledFor(_symbol, chkTelegram.Checked);

        // Polling interval (seconds) — per-symbol, default 6, per explicit request to give some
        // tickers more/less granularity than others. NumericUpDown is a textbox with built-in +/-
        // buttons, matching what was asked for. Persists immediately on change and, if this symbol
        // is the one Form1 is actively polling right now, updates that live timer's Interval too
        // (no reconnect needed) — see Form1.SetPollingIntervalFor/ApplyLivePollingInterval. Also
        // drives the Charts tab's own update cadence, since it just mirrors Form1's own poll cycle
        // (OnQuotesUpdatedEvent), not a separate timer.
        var lblPollingInterval = new Label { Text = "Poll(s)", AutoSize = true, Margin = new Padding(12, 6, 2, 3) };
        var numPollingInterval = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 60,
            Value   = Math.Clamp(Form1.GetPollingIntervalFor(_symbol), 1, 60),
            Width   = 50,
            Margin  = new Padding(2, 3, 3, 3)
        };
        numPollingInterval.ValueChanged += (s, e) =>
        {
            var seconds = (int)numPollingInterval.Value;
            Form1.SetPollingIntervalFor(_symbol, seconds);
            _form1.ApplyLivePollingInterval(_symbol, seconds);
        };

        // Live "time — price" readout for panel 2's own WebSocket ticks, above panel 2's toolbar —
        // same idea as MultiChartForm's own lblLiveTick (panel 3), per explicit request to have one
        // here too, so a stalled/disconnected feed (no updates) is visible at a glance.
        var lblRthLiveTick = new Label
        {
            Text      = string.Empty,
            AutoSize  = true,
            ForeColor = Color.DarkGoldenrod,
            Font      = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold),
            Margin    = new Padding(16, 6, 3, 3)
        };
        if (rthPanel != null)
        {
            rthPanel.OnLiveTick += (eastern, price) =>
            {
                if (IsDisposed || lblRthLiveTick.IsDisposed || !lblRthLiveTick.IsHandleCreated) return;
                lblRthLiveTick.BeginInvoke(() => lblRthLiveTick.Text = $"{eastern:HH:mm:ss}  {price:F2}");
            };
        }

        // Panel 1 group, per explicit request/order: Rect, ↑Verde, ↓Roja, Daily, Día, ATH.
        toolbarLeft.Controls.Add(btnRectGris);
        toolbarLeft.Controls.Add(btnFlechaVerde);
        toolbarLeft.Controls.Add(btnFlechaRoja);
        toolbarLeft.Controls.Add(btnDaily);
        toolbarLeft.Controls.Add(chkDayDividers);
        toolbarLeft.Controls.Add(AthCheckBox);

        // Panel 2 group, per explicit request/order: H-Line, T-Line, Text, Arrow, BB edges, AWS, Telegram.
        toolbarRight.Controls.Add(HLineButton);
        toolbarRight.Controls.Add(btnTLine);
        toolbarRight.Controls.Add(TextButton);
        toolbarRight.Controls.Add(ArrowButton);
        toolbarRight.Controls.Add(chkBollingerEdges);
        toolbarRightRow2.Controls.Add(chkAws);
        toolbarRightRow2.Controls.Add(chkTelegram);
        toolbarRightRow2.Controls.Add(lblPollingInterval);
        toolbarRightRow2.Controls.Add(numPollingInterval);
        toolbarRightRow2.Controls.Add(lblRthLiveTick);

        // Wraps the toolbar row + its Text-tool note box together so their relative order (toolbar
        // ChartTextTextBox does NOT live here — it already has its own home further down
        // (optionsGridHost.Controls.Add(ChartTextTextBox), unchanged, Dock=Bottom/Height=80 next to
        // the options grid). A control can only have one parent, so an earlier version of this
        // method that also added it here silently lost that fight to the later re-add — but left
        // its Dock mutated to Fill in the process, which is what actually shipped: a real bug
        // (broke the options-grid area's own layout). Fixed by leaving ChartTextTextBox alone here
        // entirely.
        var topStrip = new Panel { Dock = DockStyle.Top, Height = toolbar.Height };
        topStrip.Controls.Add(toolbar);

        // Small event log below the charts — logs Cross-SMA cruce/rebote detections (so the
        // Telegram-push feature can be sanity-checked without digging through Telegram itself).
        // Temporary/diagnostic for now.
        _crossLog = new TextBox
        {
            Dock       = DockStyle.Fill,
            Multiline  = true,
            ReadOnly   = true,
            ScrollBars = ScrollBars.Vertical,
            Font       = new Font("Consolas", 8.5F),
            BackColor  = Color.Black,
            ForeColor  = Color.LightGreen
        };

        // "PM" (Punto Medio) size coordination: each panel only knows its OWN SMA20 slope — whether
        // the 1h and 15m RTH panels currently agree (both bullish or both bearish) is a cross-panel
        // decision that has to live here. Track the latest direction from each and redraw BOTH with
        // a shared "large" flag once both are known — bigger text when they agree, normal otherwise.
        if (hourlyPanel != null && rthPanel != null)
        {
            bool? hourlyPmBullish = null;
            bool? rthPmBullish = null;

            void RedrawPuntoMedio()
            {
                if (hourlyPmBullish == null || rthPmBullish == null) return;
                var large = hourlyPmBullish == rthPmBullish;
                _ = hourlyPanel.MarkPuntoMedioAsync(hourlyPmBullish.Value, large);
                _ = rthPanel.MarkPuntoMedioAsync(rthPmBullish.Value, large);
            }

            hourlyPanel.OnPuntoMedioLevelEvent += bullish => { hourlyPmBullish = bullish; RedrawPuntoMedio(); };
            rthPanel.OnPuntoMedioLevelEvent += bullish => { rthPmBullish = bullish; RedrawPuntoMedio(); };

            // Backtesting aid: log the exact moment PM AND BB both agree in color (verde/rojo)
            // across the 1h and 15m RTH panels — i.e. both panels' SMA20 tilting the same direction
            // AND both panels' Bollinger Bands currently widening in that same direction. Logged
            // once per alignment episode (only on the false→true transition), not every tick while
            // it holds, so it doesn't spam the log.
            bool? hourlyBbBullish = null; // null = BB not currently shown on that panel
            bool? rthBbBullish = null;
            var pmBbAligned = false;

            void CheckPmBbAlignment()
            {
                var aligned = hourlyPmBullish != null && hourlyPmBullish == rthPmBullish
                    && hourlyBbBullish != null && hourlyBbBullish == rthBbBullish
                    && hourlyPmBullish == hourlyBbBullish;

                if (aligned && !pmBbAligned)
                {
                    var direction = hourlyPmBullish!.Value ? "Alza (verde)" : "Baja (rojo)";
                    AppendLog($"{DateTime.Now:HH:mm:ss}  PM + BB alineados en {direction} (1h y 15m RTH){Environment.NewLine}");
                }
                pmBbAligned = aligned;
            }

            hourlyPanel.OnPuntoMedioLevelEvent += _ => CheckPmBbAlignment();
            rthPanel.OnPuntoMedioLevelEvent += _ => CheckPmBbAlignment();
            hourlyPanel.OnBollingerWideningLevelEvent += (show, bullish) => { hourlyBbBullish = show ? bullish : null; CheckPmBbAlignment(); };
            rthPanel.OnBollingerWideningLevelEvent += (show, bullish) => { rthBbBullish = show ? bullish : null; CheckPmBbAlignment(); };
        }

        // Continuous RTH Piso/Techo invalidation, per explicit request: only panel 2's (Fifteen_RTH)
        // own RTH price action may invalidate a level all through the session (previously only the
        // 9:30 open snapshot did) — never panel 3's (RTH+Overnight includes overnight/extended-
        // hours moves that have nothing to do with the regular session, and lives on MultiChartForm).
        // rthPanel.OnLiveTick fires for every raw tick regardless of session, so it's filtered to RTH
        // hours here before being routed into hourlyPanel's own instance — the SMA these levels are
        // about is always panel 1's own 1h SMA, so the check must run THERE, not on rthPanel's own
        // (15m) candles.
        if (hourlyPanel != null && rthPanel != null)
        {
            rthPanel.OnLiveTick += (eastern, price) =>
            {
                if (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)) return;
                hourlyPanel.ValidatePisoTechoAgainstLivePrice(price);
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnVolatilityOpeningEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // "Ya abiertas al armar" — informational heads-up, log-only, doesn't wait for the
            // spot to actually touch a band (see ChartPanel.OnVolatilityAlreadyOpenEvent).
            rthPanel.OnVolatilityAlreadyOpenEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        // "Expuesto en 3 charts" — premarket-only: on every premarket tick (fired from the 1h
        // panel, see ChartPanel.OnPreMarketPriceUpdated), check whether that price broke the SAME
        // side (upper or lower) of the Bollinger(20,2) band on Daily, 1h AND 15m RTH all at once
        // (the "3 charts" here are Daily/1h/15m RTH — not the 3 live panels; panel 3 is unrelated).
        // Shown as a yellow banner at the top of the 15m RTH panel — hidden again the moment any
        // one of the 3 stops agreeing (re-evaluated fresh on every tick, nothing latched).
        if (hourlyPanel != null && rthPanel != null)
        {
            hourlyPanel.OnPreMarketPriceUpdated += price =>
            {
                var dailyDir  = ChartPanel.GetDailyBollingerDirection(_symbol, price);
                var hourlyDir = hourlyPanel.GetBollingerDirection(price);
                var rthDir    = rthPanel.GetBollingerDirection(price);

                var exposed = dailyDir != BollingerDirection.None && dailyDir == hourlyDir && dailyDir == rthDir;
                _ = exposed ? rthPanel.ShowExposureBannerAsync("Expuesto en 3 charts") : rthPanel.HideExposureBannerAsync();
            };
        }

        // Blue "current price" line on any open "Daily" window(s) — today's live spot, whether
        // premarket or RTH, per explicit request. hourlyPanel's OnLiveTick fires for every raw
        // 1-minute tick regardless of session.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnLiveTick += (eastern, price) =>
            {
                // BeginInvoke — this fires from Streamer_OnNewCandle's background (WebSocket)
                // thread, and UpdateLivePrice touches CoreWebView2 (same threading bug class as
                // AutoZonePush, already fixed elsewhere in this app).
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    foreach (var dailyForm in _openDailyCharts.ToList())
                        _ = dailyForm.UpdateLivePrice(price);
                });
            };
        }

        // Live options grid — same 7 fields as Form1's dgvQuotes but ONE column set shared by
        // both Calls and Puts (one row per option) instead of a call-side/put-side pair, and
        // Bid/Ask/Sprd/Conts/Level moved to the right of Strike with Range pushed to the end
        // (Form1's own grid keeps Range/Sprd/Bid/Ask BEFORE the Strike button — different order,
        // per explicit request for this one).
        // Columns + per-cell styling + Strike-button painting — identical setup for both the
        // "today" and "next" expiration grids (Fase 2 of the tabbed options-grid feature), so this
        // is factored into one local function instead of duplicated verbatim.
        void WireOptionsGrid(DataGridView grid)
        {
            grid.Columns.AddRange(
                new DataGridViewButtonColumn { Name = "colStrikeLive", HeaderText = "Strike", Width = 46, FlatStyle = FlatStyle.Standard, UseColumnTextForButtonValue = false },
                new DataGridViewTextBoxColumn { Name = "colBidLive",   HeaderText = "Bid",   Width = 38, ReadOnly = true },
                new DataGridViewTextBoxColumn { Name = "colAskLive",   HeaderText = "Ask",   Width = 38, ReadOnly = true },
                new DataGridViewTextBoxColumn { Name = "colSprdLive",  HeaderText = "Sprd",  Width = 34, ReadOnly = true },
                new DataGridViewTextBoxColumn { Name = "colContsLive", HeaderText = "Conts", Width = 40, ReadOnly = true },
                new DataGridViewTextBoxColumn { Name = "colLevelLive", HeaderText = "Level", Width = 38, ReadOnly = true },
                new DataGridViewTextBoxColumn { Name = "colRangeLive", HeaderText = "Range", Width = 70, ReadOnly = true });
            foreach (DataGridViewColumn col in grid.Columns)
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // Same per-cell styling as Form1's own dgvQuotes (DgvQuotes_CellFormatting): Sprd bold
            // red, Ask bold dark green, Bid background green when that row's Sprd <= 2.
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = grid.Rows[e.RowIndex];
                var sprdCol  = grid.Columns["colSprdLive"]!.Index;
                var bidCol   = grid.Columns["colBidLive"]!.Index;
                var askCol   = grid.Columns["colAskLive"]!.Index;
                var rangeCol = grid.Columns["colRangeLive"]!.Index;

                if (e.ColumnIndex == sprdCol)
                {
                    e.CellStyle.ForeColor = Color.Red;
                    e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                }
                else if (e.ColumnIndex == askCol)
                {
                    e.CellStyle.ForeColor = Color.DarkGreen;
                    e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                }
                else if (e.ColumnIndex == bidCol)
                {
                    e.CellStyle.BackColor = decimal.TryParse(row.Cells["colSprdLive"].Value?.ToString(), out var sprd) && sprd <= 2
                        ? Color.LightGreen
                        : grid.DefaultCellStyle.BackColor;
                }
                else if (e.ColumnIndex == rangeCol)
                {
                    // Same rule as Form1's dgvQuotes colRange (DgvQuotes_CellFormatting): green when
                    // this row's own Ask actually falls within "Low - High".
                    var rangeText = row.Cells["colRangeLive"].Value?.ToString();
                    var parts = rangeText?.Split(" - ", StringSplitOptions.TrimEntries);
                    var inRange = parts?.Length == 2
                        && decimal.TryParse(parts[0], out var low) && decimal.TryParse(parts[1], out var high)
                        && decimal.TryParse(row.Cells["colAskLive"].Value?.ToString(), out var ask)
                        && ask >= low && ask <= high;
                    e.CellStyle.BackColor = inRange ? Color.LightGreen : grid.DefaultCellStyle.BackColor;
                }
            };

            // Strike button: dark green for Call rows, red for Put rows, light gray when blocked —
            // identical to DgvQuotes_CellPainting's colStrikePrice button on Form1's own grid (same
            // IsRowTradeBlocked rule: bid == 0, OR spread >= 6, OR 0 contracts).
            grid.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != grid.Columns["colStrikeLive"]!.Index) return;

                var val     = e.Value?.ToString();
                var row     = grid.Rows[e.RowIndex];
                var rowType = row.Tag?.ToString();
                e.PaintBackground(e.ClipBounds, true);
                if (string.IsNullOrEmpty(val)) { e.Handled = true; return; }

                var disabled =
                    !decimal.TryParse(row.Cells["colBidLive"].Value?.ToString(), out var bid) || bid == 0m
                    || (decimal.TryParse(row.Cells["colSprdLive"].Value?.ToString(), out var sprd) && sprd >= 6)
                    || !int.TryParse(row.Cells["colContsLive"].Value?.ToString(), out var conts) || conts == 0;

                var btnColor  = disabled ? Color.LightGray : (rowType == "PUT" ? Color.Red : Color.DarkGreen);
                var textColor = disabled ? Color.Gray : Color.White;
                var btnRect   = Rectangle.Inflate(e.CellBounds, -3, -3);
                using var fillBrush = new SolidBrush(btnColor);
                using var borderPen = new Pen(ControlPaint.Dark(btnColor, 0.2f));
                using var textFont  = new Font(grid.Font, FontStyle.Bold);
                e.Graphics!.FillRectangle(fillBrush, btnRect);
                e.Graphics.DrawRectangle(borderPen, btnRect);
                TextRenderer.DrawText(e.Graphics, val, textFont, btnRect, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                e.Handled = true;
            };
        }
        WireOptionsGrid(_dgvOptions);
        WireOptionsGrid(_dgvOptionsNext);

        // Tab control hosting the "today" and "next" expiration grids (Fase 2 of the tabbed
        // options-grid feature) — the "Next" tab is only added while Form1.IsNextExpDateVisible is
        // true (mirrors chkHideNextExpDate on Form1), toggled in RefreshOptionsGrid below. Owner-
        // drawn so the selected tab's header can be bolded/highlighted, per explicit request.
        var tabToday = new TabPage("Hoy") { Padding = new Padding(2) };
        tabToday.Controls.Add(_dgvOptions);
        var tabNext = new TabPage("Próxima") { Padding = new Padding(2) };
        tabNext.Controls.Add(_dgvOptionsNext);
        var tabOptions = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed };
        tabOptions.TabPages.Add(tabToday);
        tabOptions.DrawItem += (s, e) =>
        {
            var page = tabOptions.TabPages[e.Index];
            var selected = e.Index == tabOptions.SelectedIndex;
            using var backBrush = new SolidBrush(selected ? Color.FromArgb(230, 244, 255) : tabOptions.BackColor);
            e.Graphics.FillRectangle(backBrush, e.Bounds);
            using var font = new Font(tabOptions.Font, selected ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, page.Text, font, e.Bounds, selected ? Color.FromArgb(0, 90, 180) : Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // No top/bottom padding, per explicit request — reach all the way up to the tab strip and
        // down to the trades grid, with no gap on either side.
        var optionsGridHost = new Panel { Dock = DockStyle.Right, Width = 345, Padding = new Padding(6, 0, 6, 0) };

        // ExpDate above the grid — same resolved value (handles "0DTE"/weekday shorthand etc.) as
        // everywhere else this ticker's expiration is shown.
        var lblExpDate = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.DarkGoldenrod
        };
        // Next expiration date — same ResolveNext used by Form1's own "next" chain grid
        // (grpOptionsChainNext), just surfaced here as a label too (Fase 1 of the tabbed
        // options-grid feature — the grid itself follows in a later phase).
        var lblExpDateNext = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.Gray
        };
        // Polling time shown in Form1's own topStrip instead (next to Disconnect — see
        // Form1.SetupChartsTab), not here: this options column's Dock=Top stacking already governs
        // ExpDate/Next, and the requested position was next to the Connect/Disconnect button, not
        // above the grid.

        // Grid fills the rest of this column — the Text-tool note box (ChartTextTextBox) moved out
        // of here entirely, per explicit request: it now sits next to the crossLog, below the
        // trades grid (see bottomSection further down), not stacked inside the options column.
        optionsGridHost.Controls.Add(tabOptions);
        optionsGridHost.Controls.Add(lblExpDateNext);
        optionsGridHost.Controls.Add(lblExpDate);

        void RefreshOptionsGrid()
        {
            var snapshot = _form1.GetQuoteSnapshot(_symbol);
            if (snapshot == null) return;
            if (_dgvOptions.IsDisposed || !_dgvOptions.IsHandleCreated) return;
            if (!lblExpDate.IsDisposed)
                lblExpDate.Text = $"ExpDate: {ExpirationDateResolver.Resolve(snapshot.Value.Ticker.ExpDate):yyyy-MM-dd}";
            if (!lblExpDateNext.IsDisposed)
                lblExpDateNext.Text = $"Next: {ExpirationDateResolver.ResolveNext(snapshot.Value.Ticker.ExpDate):yyyy-MM-dd}";

            var snapshotNext = _form1.GetQuoteSnapshotNext(_symbol);

            // Everything below is queued as ONE BeginInvoke off _dgvOptions (whose handle is
            // guaranteed created — it's part of tabToday, added to tabOptions at construction time)
            // instead of separate BeginInvoke calls per control. Doing the "Próxima" tab add/remove
            // and its grid populate in the SAME queued callback matters: TabPages.Add(tabNext)
            // creates _dgvOptionsNext's handle synchronously (its parent, tabOptions, already has
            // one), so the populate right after it can run immediately — no handle-created race
            // where the populate call gets silently skipped because tabNext hadn't been added yet
            // (which is what made the "Próxima" tab render its headers but never any rows/data).
            _dgvOptions.BeginInvoke(() =>
            {
                Form1.PopulateSingleSideOptionsGrid(
                    _dgvOptions, snapshot.Value.AllQuotes, snapshot.Value.OtmCalls, snapshot.Value.OtmPuts, snapshot.Value.Ticker);

                // "Próxima" tab: only present while Form1 itself shows the next-expiration chain
                // (mirrors chkHideNextExpDate) — added/removed here instead of once at startup so
                // toggling that checkbox on Form1 while the Live Chart is already open still takes
                // effect on the very next poll cycle.
                var shouldShowNext = _form1.IsNextExpDateVisible;
                var hasNextTab = tabOptions.TabPages.Contains(tabNext);
                if (shouldShowNext && !hasNextTab) tabOptions.TabPages.Add(tabNext);
                else if (!shouldShowNext && hasNextTab) tabOptions.TabPages.Remove(tabNext);

                if (shouldShowNext && snapshotNext != null)
                {
                    Form1.PopulateSingleSideOptionsGrid(
                        _dgvOptionsNext, snapshotNext.Value.AllQuotes, snapshotNext.Value.OtmCalls, snapshotNext.Value.OtmPuts, snapshotNext.Value.Ticker);
                }
            });
        }

        // Strike click: forwards into Form1's own click handler — opens a trade using whatever
        // radio mode (No Trade / No Trade-Target / Trade / Trade-Target) is currently selected on
        // Form1, identical behavior to clicking Strike there directly. Reads the AWS-enabled flag
        // straight from the persisted per-ticker store instead of a live "AWS" checkbox — that
        // checkbox lives on panel 3's own toolbar (MultiChartForm), which doesn't exist when this
        // control is embedded standalone on Form1's Charts tab; the checkbox is only ever a live
        // view over this same store (see MultiChartForm's chkAws.CheckedChanged), so reading the
        // store directly is equivalent whether or not that checkbox happens to exist.
        _dgvOptions.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvOptions.Columns["colStrikeLive"]!.Index) return;
            var row = _dgvOptions.Rows[e.RowIndex];
            var rowType = row.Tag?.ToString();
            var strikeText = row.Cells["colStrikeLive"].Value?.ToString();
            if (string.IsNullOrEmpty(rowType) || string.IsNullOrEmpty(strikeText)) return;
            _form1.TriggerQuoteStrikeClick(_symbol, rowType, strikeText, Form1.IsAwsEnabledFor(_symbol), _useRealTrade);
        };

        // "Próxima" tab strike clicks — same idea as _dgvOptions.CellClick above, but forwards into
        // Form1.TriggerQuoteStrikeClickNext, which opens the trade with the NEXT expiration date
        // (tomorrow, for a daily ExpDate code) instead of the ticker's default resolved date.
        _dgvOptionsNext.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvOptionsNext.Columns["colStrikeLive"]!.Index) return;
            var row = _dgvOptionsNext.Rows[e.RowIndex];
            var rowType = row.Tag?.ToString();
            var strikeText = row.Cells["colStrikeLive"].Value?.ToString();
            if (string.IsNullOrEmpty(rowType) || string.IsNullOrEmpty(strikeText)) return;
            _form1.TriggerQuoteStrikeClickNext(_symbol, rowType, strikeText, Form1.IsAwsEnabledFor(_symbol), _useRealTrade);
        };

        _form1.OnQuotesUpdatedEvent += OnForm1QuotesUpdated;
        void OnForm1QuotesUpdated(string updatedSymbol)
        {
            if (updatedSymbol == _symbol) RefreshOptionsGrid();
        }
        Disposed += (s, e) => _form1.OnQuotesUpdatedEvent -= OnForm1QuotesUpdated;
        HandleCreated += (s, e) => RefreshOptionsGrid();

        // Trades grid — exact same 17 columns as Form1's dgvTrades, mirrored below the charts.
        _dgvTrades.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "colTradeTimeLive",       HeaderText = "Time",        Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeTypeLive",       HeaderText = "Type",        Width = 45, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeStrikeLive",     HeaderText = "Strike",       Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeBidLive",       HeaderText = "Bid",          Width = 38, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeAskLive",       HeaderText = "Ask",          Width = 38, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeContractsLive", HeaderText = "Conts",        Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeEntryPriceLive",HeaderText = "Entry",        Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeCBidLive",      HeaderText = "C_Bid",        Width = 50, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeTBidLive",      HeaderText = "T_Bid",        Width = 50, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLLive",       HeaderText = "PnL",          Width = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLPercentLive",HeaderText = "PnL_Percent",  Width = 70, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLTargetLive", HeaderText = "PnL_Target",   Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeExitTimeLive",  HeaderText = "ExitTime",     Width = 60, ReadOnly = true },
            new DataGridViewButtonColumn  { Name = "colTradeCloseLive",     HeaderText = "Close",        Width = 55, FlatStyle = FlatStyle.Standard, UseColumnTextForButtonValue = false },
            new DataGridViewTextBoxColumn { Name = "colTradePnLMinLive",    HeaderText = "Min PnL%",     Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLMaxLive",    HeaderText = "Max PnL%",     Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeMoneynessLive", HeaderText = "OTM/ITM",      Width = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeDemoRealLive", HeaderText = "Demo/Real",    Width = 65, ReadOnly = true });
        foreach (DataGridViewColumn col in _dgvTrades.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Read-only mirror — never show the blue "selected row" highlight, which would otherwise
        // mask the per-cell colors copied from Form1 (see RefreshTradesGrid) the moment a row gets
        // auto-selected (Rows.Add() always leaves the newest row selected/current).
        _dgvTrades.DefaultCellStyle.SelectionBackColor = _dgvTrades.DefaultCellStyle.BackColor;
        _dgvTrades.DefaultCellStyle.SelectionForeColor = _dgvTrades.DefaultCellStyle.ForeColor;

        // Close click: forwards into Form1's own DgvTrades_CellClick (real trades place an actual
        // SELL_TO_CLOSE order; demo trades just close in the log) — identical to clicking Close on
        // Form1's own grid for the same row.
        _dgvTrades.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvTrades.Columns["colTradeCloseLive"]!.Index) return;
            if (!string.IsNullOrEmpty(_dgvTrades.Rows[e.RowIndex].Cells["colTradeExitTimeLive"].Value?.ToString())) return;
            _form1.TriggerTradeCloseClick(_symbol, e.RowIndex);
        };

        var tradesGridHost = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(6, 0, 6, 6) };

        // Grid pinned to 90% of the host's own width (per explicit request) — Anchor instead of
        // Dock=Fill so it stays put in the same top-left spot while leaving the remaining 10% as
        // blank host background on the right. Host itself stays Dock=Top/full-width so bottomSection's
        // logRow (Dock=Fill, added before this host) keeps reserving space below it exactly as before.
        _dgvTrades.Dock = DockStyle.None;
        _dgvTrades.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        void ResizeTradesGrid()
        {
            var usableWidth  = tradesGridHost.ClientSize.Width - tradesGridHost.Padding.Left - tradesGridHost.Padding.Right;
            var usableHeight = tradesGridHost.ClientSize.Height - tradesGridHost.Padding.Top - tradesGridHost.Padding.Bottom;
            _dgvTrades.Location = new Point(tradesGridHost.Padding.Left, tradesGridHost.Padding.Top);
            _dgvTrades.Width  = (int)(usableWidth * 0.9);
            _dgvTrades.Height = usableHeight;

            // Charts tab's own Demo-Target/Real-Target radios sit in the 10% freed up to the
            // right of the grid — independent of Form1's 4-way "Trade" radios (Options Quotes tab).
            var radiosX = _dgvTrades.Right + 6;
            _rbChartsDemoTarget.Location = new Point(radiosX, tradesGridHost.Padding.Top + 2);
            _rbChartsRealTarget.Location = new Point(radiosX, tradesGridHost.Padding.Top + 22);
        }
        tradesGridHost.SizeChanged += (s, e) => ResizeTradesGrid();
        tradesGridHost.Controls.Add(_dgvTrades);
        tradesGridHost.Controls.Add(_rbChartsDemoTarget);
        tradesGridHost.Controls.Add(_rbChartsRealTarget);
        _rbChartsDemoTarget.CheckedChanged += (s, e) =>
        {
            _rbChartsDemoTarget.Font = new Font(_rbChartsDemoTarget.Font, _rbChartsDemoTarget.Checked ? FontStyle.Bold : FontStyle.Regular);
            if (_rbChartsDemoTarget.Checked) _useRealTrade = false;
        };
        _rbChartsRealTarget.CheckedChanged += (s, e) =>
        {
            _rbChartsRealTarget.Font = new Font(_rbChartsRealTarget.Font, _rbChartsRealTarget.Checked ? FontStyle.Bold : FontStyle.Regular);
            if (_rbChartsRealTarget.Checked) _useRealTrade = true;
        };
        ResizeTradesGrid();

        // Full rebuild every refresh (values + per-cell colors copied straight from Form1's own
        // dgvTrades) — simplest way to stay pixel-identical to whatever coloring Form1 applies
        // (lavender automatic-trade rows, gray-on-close, PnL colors, etc.) without re-deriving
        // those rules here. Source column order matches this grid's column order 1:1.
        void RefreshTradesGrid()
        {
            var sourceGrid = _form1.GetTradesGrid(_symbol);
            if (sourceGrid == null) return;
            if (_dgvTrades.IsDisposed || !_dgvTrades.IsHandleCreated) return;
            _dgvTrades.BeginInvoke(() =>
            {
                var scrollRowToRestore = _dgvTrades.Rows.Count > 0 ? _dgvTrades.FirstDisplayedScrollingRowIndex : -1;
                _dgvTrades.Rows.Clear();
                foreach (DataGridViewRow sourceRow in sourceGrid.Rows)
                {
                    var values = sourceRow.Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray();
                    _dgvTrades.Rows.Add(values);
                    var mirrorRow = _dgvTrades.Rows[_dgvTrades.Rows.Count - 1];
                    mirrorRow.DefaultCellStyle.BackColor = sourceRow.DefaultCellStyle.BackColor;
                    mirrorRow.DefaultCellStyle.ForeColor = sourceRow.DefaultCellStyle.ForeColor;
                    for (int i = 0; i < sourceRow.Cells.Count && i < mirrorRow.Cells.Count; i++)
                    {
                        mirrorRow.Cells[i].Style.ForeColor = sourceRow.Cells[i].Style.ForeColor;
                        mirrorRow.Cells[i].Style.BackColor = sourceRow.Cells[i].Style.BackColor;
                        if (sourceRow.Cells[i].Style.Font != null)
                            mirrorRow.Cells[i].Style.Font = sourceRow.Cells[i].Style.Font;
                    }

                    // colTradeDemoReal is a hidden carrier column on Form1's own grid (see Form1's
                    // constructor) — copied in above like every other cell, but overridden here with
                    // its own fixed color since Form1 never styles it (nothing shows it there).
                    var demoRealCell = mirrorRow.Cells["colTradeDemoRealLive"];
                    demoRealCell.Style.ForeColor = string.Equals(demoRealCell.Value?.ToString(), "Real", StringComparison.OrdinalIgnoreCase)
                        ? Color.Green : Color.Orange;
                }
                if (scrollRowToRestore >= 0 && _dgvTrades.Rows.Count > 0)
                    _dgvTrades.FirstDisplayedScrollingRowIndex = Math.Min(scrollRowToRestore, _dgvTrades.Rows.Count - 1);

                // Rows.Add() leaves the first row selected/current by default, which paints it
                // with the grid's SelectionBackColor (blue) — masking the per-cell colors just
                // copied above. This is a read-only mirror, nothing should ever look "selected".
                _dgvTrades.ClearSelection();
                _dgvTrades.CurrentCell = null;
            });
        }

        _form1.OnTradesUpdatedEvent += OnForm1TradesUpdated;
        void OnForm1TradesUpdated(string updatedSymbol)
        {
            if (updatedSymbol == _symbol) RefreshTradesGrid();
        }
        Disposed += (s, e) => _form1.OnTradesUpdatedEvent -= OnForm1TradesUpdated;
        HandleCreated += (s, e) => RefreshTradesGrid();

        // logRow: crossLog (Fill — keeps the SAME effective width it always had, since that used to
        // just be "whatever's left after optionsGridHost's column", now made explicit) + the
        // Text-tool note box sitting beside it, sized to match optionsGridHost's own width so it
        // lands directly under that column — per explicit request ("deja el textbox al lado del
        // log"). bottomSection stacks the trades grid above this row, as ONE single Bottom-docked
        // unit so there's no ambiguity about width — it's simply full width, full stop.
        var logRow = new Panel { Dock = DockStyle.Fill };
        ChartTextTextBox.Dock = DockStyle.Right;
        ChartTextTextBox.Width = optionsGridHost.Width;
        logRow.Controls.Add(_crossLog);
        logRow.Controls.Add(ChartTextTextBox);

        var bottomSection = new Panel { Dock = DockStyle.Bottom, Height = tradesGridHost.Height + 90 };
        bottomSection.Controls.Add(logRow);
        bottomSection.Controls.Add(tradesGridHost);

        // optionsGridHost (Dock=Right) added BEFORE bottomSection — WinForms docks same-generation
        // siblings in reverse Controls.Add order (the later-added control claims its edge first), so
        // bottomSection reserves a full-width strip across the BOTTOM first, and optionsGridHost's
        // Right-docked column only fills whatever's left above that — extending the trades grid
        // (and now the log/note row) under where the options panel used to just sit empty, per
        // explicit request ("ver el grid hasta donde cubre la parte roja").
        Controls.Add(layout);
        Controls.Add(topStrip);
        Controls.Add(optionsGridHost);
        Controls.Add(bottomSection);
    }

    // Kept so MultiChartForm's own OnTextPlacedEvent handler for panel 3 can also drive this
    // control's internal disarm-the-other-panel-1/2 logic — see DisarmTextModeExcept below.
    private readonly Action<ChartPanel?> _disarmTextModeInternal = null!;

    // Called by MultiChartForm when its OWN panel 3 places text — disarms whichever of panel 1/2
    // is still armed (neither equals panel 3, so both get disarmed) and resets TextButton's color,
    // mirroring exactly what happens when panel 1 or 2 itself places the text.
    public void DisarmTextModeExcept(ChartPanel placedOn) => _disarmTextModeInternal(placedOn);

    // Single choke point for every crossLog write — per explicit request, nothing gets logged
    // before 9:30 AM ET (premarket), regardless of event type. Public so MultiChartForm's own
    // panel-3/combined-screenshot event wiring can log into this SAME textbox instance.
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    public void AppendLog(string text)
    {
        if (TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).TimeOfDay < new TimeSpan(9, 30, 0)) return;
        _crossLog.AppendText(text);
    }

    // T-Lines drawn on a DailyChartForm's "Hora"/"15 Min" tabs replicate onto this control's
    // corresponding live panel (1h/RTH) and persist there too — per explicit request, one-way
    // only. Public so Form1's own "Daily" button (which opens a DailyChartForm with no live chart
    // involved at all) can wire the same mirroring onto an already-open (or later-opened) live
    // chart for the same symbol; see Form1.BtnDaily_Click/BtnLiveChart_Click, and
    // MultiChartForm.AttachDailyMirroring, which just delegates here.
    public void AttachDailyMirroring(DailyChartForm dailyForm)
    {
        // Backfill: T-Lines already drawn on the Daily form BEFORE this live chart existed (the
        // common case — Form1's own "Daily" button needs no live chart open at all) wouldn't
        // otherwise appear, since each panel's own LoadSavedTLinesAsync only reads its own "1h"/
        // "RTH" tag, never "DailyHora"/"Daily15Min". Skips anything already mirrored, so calling
        // this again later (e.g. the live chart gets closed and reopened) doesn't duplicate rows.
        BackfillMirroredTLines("DailyHora", "1h", _hourlyPanel);
        BackfillMirroredTLines("Daily15Min", "RTH", _rthPanel);

        dailyForm.OnTLineDrawnEvent += (tag, t1, p1, t2, p2) =>
        {
            if (tag == "DailyHora") { if (_hourlyPanel != null) _ = _hourlyPanel.AddMirroredTLineAsync(t1, p1, t2, p2); }
            else if (tag == "Daily15Min") { if (_rthPanel != null) _ = _rthPanel.AddMirroredTLineAsync(t1, p1, t2, p2); }
        };
        dailyForm.OnTLineDeletedEvent += (tag, t1, p1, t2, p2) =>
        {
            if (tag == "DailyHora") { if (_hourlyPanel != null) _ = _hourlyPanel.RemoveMirroredTLineAsync(t1, p1, t2, p2); }
            else if (tag == "Daily15Min") { if (_rthPanel != null) _ = _rthPanel.RemoveMirroredTLineAsync(t1, p1, t2, p2); }
        };

        // SMA cross watch (Daily tab only — no toolbar buttons or marker on this control's own
        // panel 1 anymore, per explicit request) — the 1h panel already loads whatever's persisted
        // in SmaDailyWatchStore at its OWN init (see LoadHistoryAsync), independent of this control;
        // this just keeps its DETECTION in sync with LIVE toggles while both happen to be open
        // together (no marker to keep in sync here anymore, just the arm/disarm relay).
        dailyForm.OnSmaWatchChangedEvent += (period, armed) =>
        {
            if (IsDisposed) return;
            _ = _hourlyPanel?.SetSmaCrossWatchAsync(period, armed);
        };

        // "D.PM" toggle — apply immediately to whichever of panel 1/2 are open, rather than waiting
        // for the next hourly close to re-check the persisted flag.
        dailyForm.OnDailyPmLineToggledEvent += visible =>
        {
            if (IsDisposed) return;
            _ = _hourlyPanel?.SetDailyPmLineVisibleAsync(visible);
            _ = _rthPanel?.SetDailyPmLineVisibleAsync(visible);
        };

        // "D40"/"D100"/"D200" toggles — same immediate-apply idea, panel 1/2 only.
        dailyForm.OnDailySmaLineToggledEvent += (period, visible) =>
        {
            if (IsDisposed) return;
            _ = _hourlyPanel?.SetDailySmaLineVisibleAsync(period, visible);
            _ = _rthPanel?.SetDailySmaLineVisibleAsync(period, visible);
        };
    }

    private void BackfillMirroredTLines(string dailyTag, string liveTag, ChartPanel? panel)
    {
        if (panel == null) return;
        var dailyLines = TLineStore.Load(_symbol, dailyTag);
        if (dailyLines.Count == 0) return;
        var alreadyMirrored = new HashSet<(long T1, decimal P1, long T2, decimal P2)>(TLineStore.Load(_symbol, liveTag));
        foreach (var line in dailyLines)
            if (!alreadyMirrored.Contains(line))
                _ = panel.AddMirroredTLineAsync(line.T1, line.P1, line.T2, line.P2);
    }

    // Feeds a fresh spot price (from Form1's ~6s options-chain polling, not the streaming feed)
    // into panel 1/2's currently-forming candle — used while LEVEL_ONE_EQUITIES is disabled, so
    // the live chart still tracks something closer to real-time than waiting a full minute for
    // the next CHART_EQUITY bar. MultiChartForm's own FeedPollingPrice calls this AND feeds its
    // own panel 3.
    public void FeedPollingPrice(decimal price, DateTime utcTime)
    {
        _hourlyPanel?.FeedPollingPrice(price, utcTime);
        _rthPanel?.FeedPollingPrice(price, utcTime);
    }

    // Yesterday's last hourly candle (its close) — the Piso/Techo reference line's real anchor, same
    // as MultiChartForm's own copy of this helper (kept separate rather than shared, since this
    // control doesn't otherwise depend on MultiChartForm). Falls back to today 4:00 AM ET if no
    // prior-day history is on disk yet (shouldn't normally happen).
    private long GetTodaySessionStartFakeEpoch()
    {
        var hourly = HourlyCandleStore.Load(_symbol);
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).Date;
        var prior = hourly
            .Select(c => (Candle: c, Date: TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone).Date))
            .Where(x => x.Date < todayEastern)
            .ToList();

        if (prior.Count > 0)
        {
            var prevDate = prior.Max(x => x.Date);
            var lastBar = prior.Where(x => x.Date == prevDate).OrderBy(x => x.Candle.Time).Last().Candle;
            return ChartPanel.FakeUtcEpochSeconds(lastBar.Time);
        }

        var fallbackEastern = todayEastern.AddHours(4);
        var fallbackUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fallbackEastern, DateTimeKind.Unspecified), EasternZone);
        return ChartPanel.FakeUtcEpochSeconds(fallbackUtc);
    }

    // RTH session close (16:00 ET, today) — so the Piso/Techo reference line stops there instead of
    // running off to the chart's own right edge.
    private static long GetTodaySessionEndFakeEpoch()
    {
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).Date;
        var sessionEndEastern = todayEastern.AddHours(16);
        var sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionEndEastern, DateTimeKind.Unspecified), EasternZone);
        return ChartPanel.FakeUtcEpochSeconds(sessionEndUtc);
    }

    // Pushes the combined (panel 1 + 2) snapshot for a Piso/Techo Cruce/Rebote resolution — same
    // best-effort pattern as every other Telegram push in this app (MultiChartForm's own copies).
    private async Task SendPisoTechoTelegramPushAsync(string caption)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
        try
        {
            var (botToken, chatId) = TelegramSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            {
                LogTelegramPushFailure("Bot Token o Chat ID vacío");
                return;
            }

            using var combined = await CaptureCombinedChartImageAsync();
            if (combined == null)
            {
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_PisoTecho_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "PisoTechoCross", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    private void LogTelegramPushFailure(string detail)
    {
        if (IsDisposed) return;
        BeginInvoke(() => AppendLog($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
    }

    // Pushes the combined (panel 1 + 2) snapshot for a T-Line + SMA20 breakout signal — same
    // best-effort pattern as SendPisoTechoTelegramPushAsync above (MultiChartForm's own copy pushes
    // the full 3-panel image instead, when it hosts this control — see SuppressOwnTelegramPushes).
    private async Task SendTLineSignalTelegramPushAsync(string caption, string timeframe)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
        try
        {
            var (botToken, chatId) = TelegramSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            {
                LogTelegramPushFailure("Bot Token o Chat ID vacío");
                return;
            }

            using var combined = await CaptureCombinedChartImageAsync();
            if (combined == null)
            {
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_TLineSignal_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "TLineSignal", DateTime.Now));
            if (ok)
            {
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
                // Attaches this screenshot to the CT record ChartPanel.EvaluateTLineSignal already
                // resolved (Alza/Baja) — CtRecordStore.OnChanged triggers CtLogWriter to regenerate
                // the global CT.md with the image now included.
                CtRecordStore.SetImagePathForMostRecentResolved(_symbol, timeframe, path);
            }
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    // Pushes the combined (panel 1 + 2) snapshot for a Daily SMA cross watch — same pattern as
    // SendTLineSignalTelegramPushAsync above.
    private async Task SendSmaCrossTelegramPushAsync(string caption)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
        try
        {
            var (botToken, chatId) = TelegramSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            {
                LogTelegramPushFailure("Bot Token o Chat ID vacío");
                return;
            }

            using var combined = await CaptureCombinedChartImageAsync();
            if (combined == null)
            {
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_SmaCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "SmaCross", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    private static readonly string[] PanelLabels = { "1 Hour", "15Min RTH" };
    private const int PanelGap = 4;
    private static readonly Color PanelGapColor = Color.Black;
    private static readonly Color PanelLabelColor = Color.White;

    // Same combined-image logic as MultiChartForm.CaptureCombinedChartImageAsync, but for just
    // this control's own 2 panels — used as the trade Entry/Close (and market Open/Close) chart
    // snapshot whenever no popup Live Chart window is open for this symbol, per explicit request.
    public async Task<Bitmap?> CaptureCombinedChartImageAsync()
    {
        if (_hourlyPanel == null || _rthPanel == null) return null;

        var panels = new[] { _hourlyPanel, _rthPanel };
        var images = new Bitmap?[panels.Length];
        try
        {
            for (int i = 0; i < panels.Length; i++)
                images[i] = await panels[i].CaptureImageAsync();

            if (images.Any(img => img == null)) return null;

            var width = images.Sum(img => img!.Width) + PanelGap * (images.Length - 1);
            var height = images.Max(img => img!.Height);
            var combined = new Bitmap(width, height);
            using (var g = Graphics.FromImage(combined))
            using (var labelFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(PanelLabelColor))
            {
                g.Clear(PanelGapColor);
                var x = 0;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i]!;
                    g.DrawImage(img, x, 0);

                    var labelSize = g.MeasureString(PanelLabels[i], labelFont);
                    var labelX = x + (img.Width - labelSize.Width) / 2f;
                    g.DrawString(PanelLabels[i], labelFont, labelBrush, labelX, 8f);

                    x += img.Width + PanelGap;
                }
            }
            return combined;
        }
        finally
        {
            foreach (var img in images) img?.Dispose();
        }
    }
}
