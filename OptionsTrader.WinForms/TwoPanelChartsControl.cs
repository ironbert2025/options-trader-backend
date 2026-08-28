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

    private ChartPanel? _hourlyPanel;
    private ChartPanel? _rthPanel;

    public ChartPanel? HourlyPanel => _hourlyPanel;
    public ChartPanel? RthPanel => _rthPanel;

    // So Form1 can check this control is actually showing the trade's own symbol before feeding
    // it a strike/entry-spot marker — see MarkEntrySpotOnRthChartAsync below.
    public string Symbol => _symbol;

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

    // SMA20/40/100/200 "SMA Watch" toolbar buttons (panel 1) — a field (not a constructor-local)
    // so AttachDailyMirroring can also keep them in sync when armed/disarmed from a DailyChartForm.
    private readonly Dictionary<int, Button> _smaWatchButtons = new();

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

        // Toolbar strip on top — 2-column layout matching the 2 chart panels below, 1:1 ratio
        // (same relative weight the 1h/15m RTH columns had in MultiChartForm's original 2:2:3).
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 88,
            ColumnCount = 2,
            RowCount    = 1,
            Padding     = new Padding(6, 4, 6, 0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

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
                    _ = SendPisoTechoTelegramPushAsync(caption);
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
                    _ = hourlyPanel.MarkDailyPmLineAsync(price, sessionStart);
                    _ = rthPanel.MarkDailyPmLineAsync(price, sessionStart);
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
        }

        // T-Line / H-Line drawing tools for the 1h panel (column 0). T-Line and H-Line share the
        // top row (side by side); Clear/Rect sit below, arrows + Daily on the third row.
        var crossHost = new Panel { Dock = DockStyle.Fill };
        var btnTLine = new Button
        {
            Text     = "T-Line",
            Location = new Point(0, 2),
            Size     = new Size(60, 24)
        };
        btnTLine.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleTLineModeAsync();
            btnTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        // Completing a T-Line (2nd click) auto-disarms itself in chart.html — reset the button
        // color here so it doesn't stay highlighted after the fact.
        if (hourlyPanel != null) hourlyPanel.OnTLinePlacedEvent += () => btnTLine.BackColor = SystemColors.Control;

        var btnHourlyClear = new Button
        {
            Text     = "Clear",
            Location = new Point(0, 30),
            Size     = new Size(60, 24)
        };

        // Filled gray rectangle marking sideways/consolidation ranges around price+SMAs. Click
        // its border to select (yellow outline), then press Delete to remove just that one.
        var btnRectGris = new Button
        {
            Text     = "Rect",
            Location = new Point(66, 30),
            Size     = new Size(60, 24)
        };
        btnRectGris.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleRectGrisModeAsync();
            btnRectGris.BackColor = on ? Color.LightGray : SystemColors.Control;
        };
        // chart.html auto-disarms this tool itself once the 2nd click completes a rectangle — per
        // explicit request, reset the button color to match. Same pattern as the sky-blue btnRect.
        if (hourlyPanel != null) hourlyPanel.OnRectGrisPlacedEvent += () => btnRectGris.BackColor = SystemColors.Control;

        // Single-click vertical arrows: green points up, red points down, tip at the click point.
        // Click the shaft to select (yellow dashed overlay), Delete removes it.
        var btnFlechaVerde = new Button
        {
            Text     = "↑ Verde",
            Location = new Point(0, 56),
            Size     = new Size(60, 24)
        };
        var btnFlechaRoja = new Button
        {
            Text     = "↓ Roja",
            Location = new Point(66, 56),
            Size     = new Size(60, 24)
        };
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

        btnHourlyClear.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ClearDrawingsAsync();
            btnTLine.BackColor = SystemColors.Control;
            btnRectGris.BackColor = SystemColors.Control;
            btnFlechaVerde.BackColor = SystemColors.Control;
            btnFlechaRoja.BackColor = SystemColors.Control;
        };

        // Toggles the 1h panel between Daily (last 20 days, aggregated from up to ~200 trading
        // days of persisted hourly history) and plain Hourly candles.
        var btnDaily = new Button
        {
            Text     = "Daily",
            Location = new Point(132, 56),
            Size     = new Size(70, 24)
        };
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
        var chkDayDividers = new CheckBox { Text = "Día", Location = new Point(208, 60), AutoSize = true, Checked = true };
        chkDayDividers.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ToggleDayDividersAsync();
        };

        // Shows/hides the ATH reference line — drawn on all 3 panels (panel 3 lives on
        // MultiChartForm, which attaches its own additional CheckedChanged handler onto this same
        // checkbox — see MultiChartForm's constructor).
        AthCheckBox = new CheckBox { Text = "ATH", Location = new Point(208, 78), AutoSize = true, Checked = true };
        AthCheckBox.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel != null) await hourlyPanel.SetAllTimeHighVisibleAsync(AthCheckBox.Checked);
            if (rthPanel != null) await rthPanel.SetAllTimeHighVisibleAsync(AthCheckBox.Checked);
        };

        // "SMA Watch" — arm/disarm a live-price-cross watch on SMA20/40/100/200 (Daily-timeframe,
        // same as DailyChartForm's own buttons — this panel does the actual monitoring regardless
        // of which window armed it) directly from the live chart, without needing the Daily popup
        // window open, per explicit request. See ChartPanel.SetSmaCrossWatchAsync.
        {
            var armedNow = SmaDailyWatchStore.Load(_symbol).ToHashSet();
            int smaX = 0;
            foreach (var period in new[] { 20, 40, 100, 200 })
            {
                var btn = new Button
                {
                    Text = $"SMA{period}",
                    Location = new Point(smaX, 104),
                    Size = new Size(60, 24),
                    BackColor = armedNow.Contains(period) ? Color.LightYellow : SystemColors.Control
                };
                smaX += 66;
                _smaWatchButtons[period] = btn;
                btn.Click += async (s, e) =>
                {
                    if (hourlyPanel == null) return;
                    var armed = btn.BackColor != Color.LightYellow;
                    await hourlyPanel.SetSmaCrossWatchAsync(period, armed);
                    btn.BackColor = armed ? Color.LightYellow : SystemColors.Control;
                };
                crossHost.Controls.Add(btn);
            }
        }
        // Deleting the 👁 marker (Delete key) on the chart itself disarms it too — keep this
        // toolbar's own button color in sync when that happens.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnSmaWatchChangedEvent += (period, armed) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (_smaWatchButtons.TryGetValue(period, out var btn))
                        btn.BackColor = armed ? Color.LightYellow : SystemColors.Control;
                });
            };
        }

        crossHost.Controls.Add(btnTLine);
        crossHost.Controls.Add(btnHourlyClear);
        crossHost.Controls.Add(btnRectGris);
        crossHost.Controls.Add(btnFlechaVerde);
        crossHost.Controls.Add(btnFlechaRoja);
        crossHost.Controls.Add(btnDaily);
        crossHost.Controls.Add(chkDayDividers);
        crossHost.Controls.Add(AthCheckBox);
        toolbar.Controls.Add(crossHost, 0, 0);

        // T-Line drawing tool for the 15m RTH panel (column 1) — no persistence like the 1h
        // panel's T-Line, just draw + Clear for this session.
        var rthToolsHost = new Panel { Dock = DockStyle.Fill };
        var btnRthTLine = new Button
        {
            Text     = "T-Line",
            Location = new Point(0, 4),
            Size     = new Size(60, 24)
        };
        HLineButton = new Button
        {
            Text     = "H-Line",
            Location = new Point(66, 4),
            Size     = new Size(60, 24)
        };
        var btnRthClear = new Button
        {
            Text     = "Clear",
            Location = new Point(132, 4),
            Size     = new Size(60, 24)
        };
        // Single "Text" button (above the 15m RTH panel, same convention as H-Line) arms
        // text-placement mode on all 3 panels at once — no mirroring, each panel only places text
        // where IT was clicked. Reads the Windows clipboard fresh each time this button is pressed.
        // Source text for the "Text" tool below — declared here (used by TextButton.Click) but only
        // actually added to the layout further down.
        ChartTextTextBox = new TextBox
        {
            Dock       = DockStyle.Bottom,
            Height     = 80,
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

        TextButton = new Button
        {
            Text     = "Text",
            Location = new Point(198, 4),
            Size     = new Size(60, 24)
        };
        ArrowButton = new Button
        {
            Text     = "Arrow",
            Location = new Point(264, 4),
            Size     = new Size(60, 24)
        };
        btnRthTLine.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            var on = await rthPanel.ToggleTLineModeAsync();
            btnRthTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        if (rthPanel != null) rthPanel.OnTLinePlacedEvent += () => btnRthTLine.BackColor = SystemColors.Control;
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
        btnRthClear.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            await rthPanel.ClearDrawingsAsync();
            btnRthTLine.BackColor = SystemColors.Control;
            HLineButton.BackColor = SystemColors.Control;
            TextButton.BackColor = SystemColors.Control;
            ArrowButton.BackColor = SystemColors.Control;
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

        // Brings every "Live Charts — <Symbol>" window to the front, across ALL running ticker
        // instances (each is a separate OS process) — not just this one's own windows. Uses raw
        // Win32 window enumeration (CrossProcessWindowHelper) since a normal BringToFront() can't
        // reach windows owned by another process.
        var btnBringAllForward = new Button
        {
            Text     = "Traer todas",
            Location = new Point(0, 30),
            Size     = new Size(126, 24)
        };
        btnBringAllForward.Click += (s, e) =>
            CrossProcessWindowHelper.BringAllToFront("Live Charts — ");

        // Shows/hides the white Bollinger-band edge markers on this panel — checked by default
        // (matches the always-on behavior before this toggle existed).
        var chkBollingerEdges = new CheckBox { Text = "BB edges", Location = new Point(132, 34), AutoSize = true, Checked = true };
        chkBollingerEdges.CheckedChanged += async (s, e) =>
        {
            if (rthPanel == null) return;
            await rthPanel.SetBollingerEdgeMarkersVisibleAsync(chkBollingerEdges.Checked);
        };

        rthToolsHost.Controls.Add(btnRthTLine);
        rthToolsHost.Controls.Add(HLineButton);
        rthToolsHost.Controls.Add(btnRthClear);
        rthToolsHost.Controls.Add(TextButton);
        rthToolsHost.Controls.Add(ArrowButton);
        rthToolsHost.Controls.Add(btnBringAllForward);
        rthToolsHost.Controls.Add(chkBollingerEdges);
        toolbar.Controls.Add(rthToolsHost, 1, 0);

        // Small event log below the charts — logs Cross-SMA cruce/rebote detections (so the
        // Telegram-push feature can be sanity-checked without digging through Telegram itself).
        // Temporary/diagnostic for now.
        _crossLog = new TextBox
        {
            Dock       = DockStyle.Bottom,
            Height     = 90,
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

        var optionsGridHost = new Panel { Dock = DockStyle.Right, Width = 345, Padding = new Padding(6, 0, 6, 6) };

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
        optionsGridHost.Controls.Add(tabOptions);
        optionsGridHost.Controls.Add(ChartTextTextBox);
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
            _form1.TriggerQuoteStrikeClick(_symbol, rowType, strikeText, Form1.IsAwsEnabledFor(_symbol));
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
            _form1.TriggerQuoteStrikeClickNext(_symbol, rowType, strikeText, Form1.IsAwsEnabledFor(_symbol));
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
            new DataGridViewTextBoxColumn { Name = "colTradeStrikeLive",     HeaderText = "StrikePrice", Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeBidLive",       HeaderText = "Bid",          Width = 45, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeAskLive",       HeaderText = "Ask",          Width = 45, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeContractsLive", HeaderText = "Contracts",    Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeEntryPriceLive",HeaderText = "EntryPrice",   Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeCBidLive",      HeaderText = "C_Bid",        Width = 50, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeTBidLive",      HeaderText = "T_Bid",        Width = 50, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLLive",       HeaderText = "PnL",          Width = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLPercentLive",HeaderText = "PnL_Percent",  Width = 70, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLTargetLive", HeaderText = "PnL_Target",   Width = 65, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeExitTimeLive",  HeaderText = "ExitTime",     Width = 60, ReadOnly = true },
            new DataGridViewButtonColumn  { Name = "colTradeCloseLive",     HeaderText = "Close",        Width = 55, FlatStyle = FlatStyle.Standard, UseColumnTextForButtonValue = false },
            new DataGridViewTextBoxColumn { Name = "colTradePnLMinLive",    HeaderText = "Min PnL%",     Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradePnLMaxLive",    HeaderText = "Max PnL%",     Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colTradeMoneynessLive", HeaderText = "OTM/ITM",      Width = 55, ReadOnly = true });
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

        var tradesGridHost = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(6, 0, 6, 6) };
        tradesGridHost.Controls.Add(_dgvTrades);

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

        Controls.Add(layout);
        Controls.Add(toolbar);
        Controls.Add(tradesGridHost);
        Controls.Add(_crossLog);
        Controls.Add(optionsGridHost);
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

        // SMA cross watch (Daily tab only) — the 1h panel already loads whatever's persisted in
        // SmaDailyWatchStore at its OWN init (see LoadHistoryAsync), independent of this control;
        // this just keeps it in sync with LIVE toggles while both happen to be open together.
        dailyForm.OnSmaWatchChangedEvent += (period, armed) =>
        {
            if (IsDisposed) return;
            _ = _hourlyPanel?.SetSmaCrossWatchAsync(period, armed);
            if (_smaWatchButtons.TryGetValue(period, out var btn))
                btn.BackColor = armed ? Color.LightYellow : SystemColors.Control;
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
