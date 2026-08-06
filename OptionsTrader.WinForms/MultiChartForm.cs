using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;
using System.Linq;

namespace OptionsTrader.WinForms;

// Single window (one per ticker) hosting the 3 live-chart panels (1h / 15m RTH / 15m
// RTH+Overnight) side by side horizontally.
//
// historyClient is used only for one-off REST history fetches (no per-account limit on that —
// every app instance/process can freely call it). liveFeed is the actual source of live ticks:
// in the app instance that owns the one Schwab streaming connection allowed per account (the
// "hub"), it's the same SchwabStreamerClient; in every OTHER running instance, it's a
// CandleHubClient relaying the hub instance's connection over a local loopback socket — this
// form/ChartPanel don't need to know which.
public class MultiChartForm : Form
{
    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly string _symbol;

    // Kept for CaptureCombinedChartImageAsync (trade snapshot) — same 3 instances the
    // constructor's local variables of the same names point to, just also reachable afterward.
    private ChartPanel? _hourlyPanel;
    private ChartPanel? _rthPanel;
    private ChartPanel? _overnightPanel;

    // Kept for LogWebSocketEvent (Form1 forwards WS connect/disconnect/reconnect events here,
    // since the hub's Schwab connection isn't owned by this window).
    private TextBox? _crossLog;

    public MultiChartForm(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;

        Text          = $"Live Charts — {symbol}";
        Width         = 1050; // +150 so the 1h/15m RTH columns keep their size while RTH+Overnight gets 50% wider
        Height        = 530;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control; // visible in the gaps between/around the 3 panels

        // Toolbar strip on top — same 3-column layout as the charts below, so each column's
        // controls line up with the chart panel directly beneath it.
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 88,
            ColumnCount = 3,
            RowCount    = 1,
            Padding     = new Padding(6, 4, 6, 0)
        };
        // 2:2:3 ratio (of 7 total) — 1h and 15m RTH stay equal, RTH+Overnight is 50% wider than
        // them (3 vs 2), so the price action there reads more clearly.
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 200f / 7));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 200f / 7));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 300f / 7));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
            Padding     = new Padding(6, 2, 6, 6)
        };
        // Same 2:2:3 ratio as the toolbar above, so each column still lines up with its buttons.
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 200f / 7));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 200f / 7));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 300f / 7));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ChartPanel? overnightPanel = null;
        ChartPanel? hourlyPanel = null;
        ChartPanel? rthPanel = null;
        var modes = new[] { ChartPanelMode.Hourly15, ChartPanelMode.Fifteen_RTH, ChartPanelMode.Fifteen_Full };
        for (int i = 0; i < modes.Length; i++)
        {
            // All 3 panels share the SAME historyClient/liveFeed — they only ever read events /
            // call the stateless REST history method, never each other's connection state.
            var panel = new ChartPanel(symbol, _historyClient, _liveFeed, modes[i])
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6, 2, 6, 6)
            };
            layout.Controls.Add(panel, i, 0);
            if (modes[i] == ChartPanelMode.Fifteen_Full) overnightPanel = panel;
            if (modes[i] == ChartPanelMode.Hourly15) hourlyPanel = panel;
            if (modes[i] == ChartPanelMode.Fifteen_RTH) rthPanel = panel;
        }
        _hourlyPanel    = hourlyPanel;
        _rthPanel       = rthPanel;
        _overnightPanel = overnightPanel;

        // Cross-SMA monitors: one toggle per period (20/40/100/200) in the toolbar column above
        // the 1h panel (column 0) — the direction (UP or DOWN) is picked automatically from where
        // price currently sits relative to that SMA when the button is armed, instead of having
        // separate UP/DOWN buttons. While armed, it pushes the 1h chart to Telegram the moment a
        // candle closes with a genuine crossover in that direction.
        var crossHost = new Panel { Dock = DockStyle.Fill };
        var periods = new[] { 20, 40, 100, 200 };
        var smaButtons = new List<Button>();
        for (int col = 0; col < periods.Length; col++)
        {
            var period = periods[col];

            var btnSma = new Button
            {
                Text     = $"{period}",
                Location = new Point(col * 42, 2),
                Size     = new Size(40, 24)
            };
            btnSma.Click += (s, e) =>
            {
                if (hourlyPanel == null) return;
                var (armed, up) = hourlyPanel.ToggleCrossMonitor(period);
                if (armed)
                {
                    btnSma.Text = up ? $"↑{period}" : $"↓{period}";
                    btnSma.BackColor = up ? Color.LightGreen : Color.LightSalmon;
                }
                else
                {
                    btnSma.Text = $"{period}";
                    btnSma.BackColor = SystemColors.Control;
                }
            };

            smaButtons.Add(btnSma);
            crossHost.Controls.Add(btnSma);
        }

        // Once the cross/bounce sequence resolves its last armed period, it stops responding for
        // the rest of the session — reset all 4 buttons back to neutral so the UI doesn't keep
        // showing them as armed when they no longer do anything.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnCrossSequenceFinished += () =>
            {
                // Streamer_OnNewCandle (where this fires from) runs on whatever thread the live
                // feed raises its event on, not necessarily the UI thread.
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    foreach (var (btn, period) in smaButtons.Zip(periods))
                    {
                        btn.Text = $"{period}";
                        btn.BackColor = SystemColors.Control;
                    }
                });
            };
        }

        // T-Line / H-Line drawing tools, also on the 1h panel — placed to the right of the 8
        // Cross-SMA toggles. T-Line and H-Line share the top row (side by side); Clear sits
        // directly below T-Line on the second row.
        var toolsStartX = periods.Length * 42 + 6;
        var btnTLine = new Button
        {
            Text     = "T-Line",
            Location = new Point(toolsStartX, 2),
            Size     = new Size(60, 24)
        };
        btnTLine.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleTLineModeAsync();
            btnTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };

        var btnHLine = new Button
        {
            Text     = "H-Line",
            Location = new Point(toolsStartX + 66, 2),
            Size     = new Size(60, 24)
        };
        btnHLine.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleHLineModeAsync();
            btnHLine.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };

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

        // Writes orange "Piso"/"Techo" text at the clicked point — one click per label, both
        // independently toggleable (same pattern as the other drawing tools).
        var btnPiso = new Button
        {
            Text     = "Piso",
            Location = new Point(toolsStartX, 30),
            Size     = new Size(60, 24)
        };
        var btnTecho = new Button
        {
            Text     = "Techo",
            Location = new Point(toolsStartX + 66, 30),
            Size     = new Size(60, 24)
        };
        btnPiso.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.TogglePisoModeAsync();
            btnPiso.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        btnTecho.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleTechoModeAsync();
            btnTecho.BackColor = on ? Color.Orange : SystemColors.Control;
        };

        // Single-click vertical arrows: green points up, red points down, tip at the click point.
        // Click the shaft to select (yellow dashed overlay), Delete removes it.
        var btnFlechaVerde = new Button
        {
            Text     = "↑ Verde",
            Location = new Point(toolsStartX, 56),
            Size     = new Size(60, 24)
        };
        var btnFlechaRoja = new Button
        {
            Text     = "↓ Roja",
            Location = new Point(toolsStartX + 66, 56),
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

        btnHourlyClear.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ClearDrawingsAsync();
            btnTLine.BackColor = SystemColors.Control;
            btnHLine.BackColor = SystemColors.Control;
            btnRectGris.BackColor = SystemColors.Control;
            btnPiso.BackColor = SystemColors.Control;
            btnTecho.BackColor = SystemColors.Control;
            btnFlechaVerde.BackColor = SystemColors.Control;
            btnFlechaRoja.BackColor = SystemColors.Control;
        };

        // Toggles the 1h panel between Daily (last 20 days, aggregated from up to ~200 trading
        // days of persisted hourly history) and plain Hourly candles. Sits in the space below the
        // Cross-SMA grid (which only uses the first 2 rows in this column).
        var btnDaily = new Button
        {
            Text     = "Daily",
            Location = new Point(0, 56),
            Size     = new Size(70, 24)
        };
        btnDaily.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            var on = await hourlyPanel.ToggleDailyModeAsync();
            btnDaily.BackColor = on ? Color.LightBlue : SystemColors.Control;
        };

        crossHost.Controls.Add(btnTLine);
        crossHost.Controls.Add(btnHLine);
        crossHost.Controls.Add(btnHourlyClear);
        crossHost.Controls.Add(btnRectGris);
        crossHost.Controls.Add(btnFlechaVerde);
        crossHost.Controls.Add(btnFlechaRoja);
        crossHost.Controls.Add(btnPiso);
        crossHost.Controls.Add(btnTecho);
        crossHost.Controls.Add(btnDaily);
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
        var btnRthHLine = new Button
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
        btnRthTLine.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            var on = await rthPanel.ToggleTLineModeAsync();
            btnRthTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        btnRthHLine.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            var on = await rthPanel.ToggleHLineModeAsync();
            btnRthHLine.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };
        btnRthClear.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            await rthPanel.ClearDrawingsAsync();
            btnRthTLine.BackColor = SystemColors.Control;
            btnRthHLine.BackColor = SystemColors.Control;
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

        rthToolsHost.Controls.Add(btnRthTLine);
        rthToolsHost.Controls.Add(btnRthHLine);
        rthToolsHost.Controls.Add(btnRthClear);
        rthToolsHost.Controls.Add(btnBringAllForward);
        toolbar.Controls.Add(rthToolsHost, 1, 0);

        // Drawing tools — all only apply to the 15m RTH+Overnight panel, so they live in the
        // toolbar column above that panel (column index 2, matching Fifteen_Full's position in
        // the layout below). A plain Dock=Fill Panel holds them so Clear can anchor to the right.
        var toolsHost = new Panel { Dock = DockStyle.Fill };

        var btnDzSz = new Button
        {
            Text     = "DZ/SZ",
            Location = new Point(0, 4),
            Size     = new Size(70, 24)
        };
        var btnRect = new Button
        {
            Text     = "Rect",
            Location = new Point(76, 4),
            Size     = new Size(70, 24)
        };
        var btnClear = new Button
        {
            Text   = "Clear",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size   = new Size(70, 24)
        };
        btnClear.Location = new Point(toolsHost.Width - btnClear.Width, 4);

        // Toggles the 15m RTH+Overnight panel between 5-minute and 15-minute candles — label
        // stays "5Min" (same toggle-button convention as DZ/SZ/T-Line/etc.), BackColor shows
        // whether 5m is currently active.
        var btn5Min = new Button
        {
            Text     = "5Min",
            Location = new Point(0, 30),
            Size     = new Size(70, 24)
        };

        // Draws a line + arrowhead between 2 clicks — red if the 1st click is above the 2nd,
        // green otherwise. Same toggle pattern as the rest.
        var btnArrow = new Button
        {
            Text     = "Arrow",
            Location = new Point(76, 30),
            Size     = new Size(70, 24)
        };

        // Live "time — price" readout, updated on every raw tick this panel receives via the
        // WebSocket — not tied to candle formation, so it updates even mid-bucket.
        var lblLiveTick = new Label
        {
            Text      = string.Empty,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location  = new Point(0, 64),
            Size      = new Size(toolsHost.Width, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Consolas", 9F),
            ForeColor = Color.DeepSkyBlue
        };
        if (overnightPanel != null)
        {
            overnightPanel.OnLiveTick += (eastern, price) =>
            {
                if (lblLiveTick.IsDisposed || !lblLiveTick.IsHandleCreated) return;
                lblLiveTick.BeginInvoke(() => lblLiveTick.Text = $"{eastern:HH:mm:ss}  {price:F2}");
            };
        }

        btnDzSz.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleDzSzModeAsync();
            btnDzSz.BackColor = on ? Color.LightGreen : SystemColors.Control;
        };
        btnRect.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleRectModeAsync();
            btnRect.BackColor = on ? Color.LightSkyBlue : SystemColors.Control;
        };
        btnClear.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            await overnightPanel.ClearDrawingsAsync();
            btnDzSz.BackColor = SystemColors.Control;
            btnRect.BackColor = SystemColors.Control;
            btnArrow.BackColor = SystemColors.Control;
        };
        btn5Min.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var is5Min = await overnightPanel.ToggleIntervalAsync();
            btn5Min.BackColor = is5Min ? Color.LightBlue : SystemColors.Control;
        };
        btnArrow.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleArrowModeAsync();
            btnArrow.BackColor = on ? Color.LightYellow : SystemColors.Control;
        };

        toolsHost.Controls.Add(btnDzSz);
        toolsHost.Controls.Add(btnRect);
        toolsHost.Controls.Add(btnClear);
        toolsHost.Controls.Add(btn5Min);
        toolsHost.Controls.Add(btnArrow);
        toolsHost.Controls.Add(lblLiveTick);
        toolbar.Controls.Add(toolsHost, 2, 0);

        // Small event log below the charts — logs Cross-SMA cruce/rebote detections (so the
        // Telegram-push feature can be sanity-checked without digging through Telegram itself)
        // and, via LogWebSocketEvent, WS connect/disconnect/reconnect events forwarded from
        // Form1's hub connection. Temporary/diagnostic for now.
        var crossLog = new TextBox
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
        if (hourlyPanel != null)
        {
            hourlyPanel.OnCrossSequenceEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // T-Line + SMA20 breakout — unlike Cross-SMA (which only pushes this panel's own
            // screenshot), the user wants the combined 3-chart image, same as a trade close.
            hourlyPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
                _ = SendTLineSignalTelegramPushAsync(message);
            };

            // Daily-candle bounce off SMA20 — purely informational, log only (no Telegram, no
            // automatic action; the user checks this window in the morning and acts manually).
            hourlyPanel.OnDailyBounceEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        // Demand Zone rebote (15m RTH+Overnight panel) — self-contained in ChartPanel (pushes its
        // own screenshot to Telegram + EventLogStore, same as Cross-SMA); just mirror the caption
        // into this window's log too.
        if (overnightPanel != null)
        {
            overnightPanel.OnDemandZoneReboundEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        // "Abriendo la Volatilidad": when the 1h panel resolves a Piso/Techo watch (any SMA
        // period), arm the 15m RTH panel's Bollinger-widening watch in the direction the
        // resolution implies price is now headed. Cruce en Techo (breaks up through resistance)
        // and Rebote en Piso (bounces up off support) are both bullish/CALL (upper band). Cruce
        // en Piso (breaks down through support) and Rebote en Techo (rejected down off
        // resistance) are both bearish/PUT (lower band) — see
        // ChartPanel.ArmVolatilityOpeningWatch/EvaluateVolatilityOpening. Also mirrors the
        // resolution's caption into crossLog (only the pre-market Piso/Techo LABELS are
        // chart-only) with the armed direction appended on the same line, so it's clear at a
        // glance what the next expected event is — crossLog-only, doesn't touch the Telegram
        // caption or what gets persisted to EventLogStore/EventLogMarkdownWriter.
        if (hourlyPanel != null && rthPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                var bullish = pisoTecho == "Techo" ? evento == "Cruce" : evento == "Rebote";
                rthPanel.ArmVolatilityOpeningWatch(bullish);

                if (IsDisposed) return;
                var direction = bullish ? "Alza" : "Baja";
                BeginInvoke(() => crossLog.AppendText(
                    $"{DateTime.Now:HH:mm:ss}  {caption} — evaluando Abriendo la Volatilidad ({direction}){Environment.NewLine}"));
            };
        }
        else if (hourlyPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnVolatilityOpeningEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        // Piso/Techo reference line: mirrors each armed SMA's pre-market level onto BOTH the 15m
        // RTH and RTH+Overnight panels (dashed, same color as that SMA on the 1h panel) — visual
        // reference for "how far price could go and bounce" without needing the 1h panel open.
        // Removed automatically if the market-open gap later invalidates that SMA.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnPisoTechoLevelReadyEvent += (period, price) =>
            {
                if (rthPanel != null) _ = rthPanel.MarkPisoTechoRefLineAsync(period, price);
                if (overnightPanel != null) _ = overnightPanel.MarkPisoTechoRefLineAsync(period, price);
            };
            hourlyPanel.OnPisoTechoLevelRemovedEvent += period =>
            {
                if (rthPanel != null) _ = rthPanel.RemovePisoTechoRefLineAsync(period);
                if (overnightPanel != null) _ = overnightPanel.RemovePisoTechoRefLineAsync(period);
            };
        }

        // Telegram push failures previously vanished silently (fire-and-forget from every call
        // site) — mirror the failure detail into crossLog on whichever panel it happened on, so a
        // missed push is diagnosable instead of just "the event logged but nothing arrived".
        foreach (var panel in new[] { hourlyPanel, rthPanel, overnightPanel })
        {
            if (panel == null) continue;
            panel.OnTelegramPushFailedEvent += detail =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
            };
        }

        Controls.Add(layout);
        Controls.Add(toolbar);
        Controls.Add(crossLog);
        _crossLog = crossLog;

        // historyClient/liveFeed are owned by Form1 for the app's whole lifetime (connecting,
        // subscribing, and disposing them) — not this window.
    }

    // Feeds a fresh spot price (from Form1's ~6s options-chain polling, not the streaming feed)
    // into all 3 panels' currently-forming candle — used while LEVEL_ONE_EQUITIES is disabled, so
    // the live chart still tracks something closer to real-time than waiting a full minute for
    // the next CHART_EQUITY bar.
    public void FeedPollingPrice(decimal price, DateTime utcTime)
    {
        _hourlyPanel?.FeedPollingPrice(price, utcTime);
        _rthPanel?.FeedPollingPrice(price, utcTime);
        _overnightPanel?.FeedPollingPrice(price, utcTime);
    }

    // Red "Expired!!!" marker on the 15m RTH panel (middle chart) only — fired when a trade
    // auto-closes at 4pm ET because it expires today.
    public Task MarkExpiredOnRthChartAsync() => _rthPanel?.MarkExpiredAsync() ?? Task.CompletedTask;

    // "ΔS=value" label at trade close — panel 3 (15m RTH+Overnight) only, per explicit request.
    public Task MarkDeltaSOnOvernightChartAsync(decimal entrySpot, decimal closeSpot, decimal strike) =>
        _overnightPanel?.MarkDeltaSAsync(entrySpot, closeSpot, strike) ?? Task.CompletedTask;

    // Green "Stk=xxx" line on all 3 panels — fired when a trade (demo or real) opens.
    public async Task MarkStrikeOnAllChartsAsync(decimal strike)
    {
        var tasks = new[]
        {
            _hourlyPanel?.MarkStrikeAsync(strike) ?? Task.CompletedTask,
            _rthPanel?.MarkStrikeAsync(strike) ?? Task.CompletedTask,
            _overnightPanel?.MarkStrikeAsync(strike) ?? Task.CompletedTask
        };
        await Task.WhenAll(tasks);
    }

    // Forwards an already-timestamped WS connect/disconnect/reconnect line from Form1 (which owns
    // the actual Schwab streamer connection) into this window's small event log — safe to call
    // from any thread, since streamer reconnects fire from its own background receive-loop thread.
    public void LogWebSocketEvent(string line)
    {
        if (_crossLog == null || IsDisposed) return;
        // ReplayWebSocketEvents is called right after construction, before Show() — the window
        // has no handle yet at that point, so IsHandleCreated must NOT gate this (it used to,
        // which silently dropped every replayed line). InvokeRequired safely returns false when
        // there's no handle yet, so this still routes through BeginInvoke once one exists.
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(() => LogWebSocketEvent(line)); return; }
        _crossLog.AppendText(line + Environment.NewLine);
    }

    // Replays every WS event Form1 has buffered so far — the streamer connects once when the
    // FIRST Live Charts window of the session is opened (before this window even exists), so
    // without this the "Connected" line would otherwise be lost for every window but that first.
    public void ReplayWebSocketEvents(IEnumerable<string> lines)
    {
        foreach (var line in lines) LogWebSocketEvent(line);
    }

    // Pushes the combined 3-chart snapshot to Telegram for the T-Line+SMA20 breakout signal —
    // best-effort, same as every other Telegram push in this app: a failure here must never
    // affect the chart/detection logic itself.
    private async Task SendTLineSignalTelegramPushAsync(string caption)
    {
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
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
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            // Best-effort — never let a Telegram failure affect the chart/detection logic, but no
            // longer silent: mirrored into crossLog same as every other push failure.
            LogTelegramPushFailure(ex.Message);
        }
    }

    private void LogTelegramPushFailure(string detail)
    {
        if (IsDisposed || _crossLog == null) return;
        BeginInvoke(() => _crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
    }

    // Renders the 3 charts (via WebView2, not a screen capture) and stitches them side by side in
    // the same left-to-right order they're shown on screen (1h, 15m RTH, 15m RTH+Overnight), for
    // Form1 to save as a single trade snapshot. Returns null if any panel isn't ready.
    // Gray gap between panels (so each chart is visually distinct in the combined snapshot) and a
    // yellow timeframe label centered at the top of each one — order matches the panels array.
    private const int PanelGap = 6;
    private static readonly Color PanelGapColor = Color.FromArgb(58, 58, 58);
    private static readonly Color PanelLabelColor = Color.FromArgb(245, 216, 0);
    private static readonly string[] PanelLabels = { "1 Hour", "15Min RTH", "15Min RTH+OVN" };

    public async Task<Bitmap?> CaptureCombinedChartImageAsync()
    {
        if (_hourlyPanel == null || _rthPanel == null || _overnightPanel == null) return null;

        var panels = new[] { _hourlyPanel, _rthPanel, _overnightPanel };
        var images = new Bitmap[panels.Length];
        try
        {
            for (int i = 0; i < panels.Length; i++)
                images[i] = await panels[i].CaptureImageAsync();

            var width  = images.Sum(img => img.Width) + PanelGap * (images.Length - 1);
            var height = images.Max(img => img.Height);
            var combined = new Bitmap(width, height);
            using (var g = Graphics.FromImage(combined))
            using (var labelFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(PanelLabelColor))
            {
                g.Clear(PanelGapColor);
                var x = 0;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
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
