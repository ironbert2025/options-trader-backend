using OptionsTrader.Application.DTOs.Streaming;
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
    private readonly Form1 _form1;

    // Kept for CaptureCombinedChartImageAsync (trade snapshot) — same 3 instances the
    // constructor's local variables of the same names point to, just also reachable afterward.
    private ChartPanel? _hourlyPanel;
    private ChartPanel? _rthPanel;
    private ChartPanel? _overnightPanel;

    private TextBox? _crossLog;

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

    public MultiChartForm(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, Form1 form1)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _form1         = form1;

        Text          = $"Live Charts — {symbol}";
        Width         = 1395; // +345 for the live options grid on the right
        Height        = 620; // +90 for the mirrored trades grid below the charts (2 rows tall)
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
            new DailyChartForm(_symbol, dailyCandles).Show();
        };

        // Dashed vertical lines separating the 7 hourly candles of each trading day (last 4 lines
        // = last 5 days; today's candles sit unbounded to the right of the most recent one).
        var chkDayDividers = new CheckBox { Text = "Día", Location = new Point(208, 60), AutoSize = true, Checked = true };
        chkDayDividers.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ToggleDayDividersAsync();
        };

        // Shows/hides the ATH reference line — drawn on all 3 panels, so this single checkbox
        // toggles all 3 at once instead of needing one per panel.
        var chkAth = new CheckBox { Text = "ATH", Location = new Point(208, 78), AutoSize = true, Checked = true };
        chkAth.CheckedChanged += async (s, e) =>
        {
            if (hourlyPanel != null) await hourlyPanel.SetAllTimeHighVisibleAsync(chkAth.Checked);
            if (rthPanel != null) await rthPanel.SetAllTimeHighVisibleAsync(chkAth.Checked);
            if (overnightPanel != null) await overnightPanel.SetAllTimeHighVisibleAsync(chkAth.Checked);
        };

        crossHost.Controls.Add(btnTLine);
        crossHost.Controls.Add(btnHourlyClear);
        crossHost.Controls.Add(btnRectGris);
        crossHost.Controls.Add(btnFlechaVerde);
        crossHost.Controls.Add(btnFlechaRoja);
        crossHost.Controls.Add(btnDaily);
        crossHost.Controls.Add(chkDayDividers);
        crossHost.Controls.Add(chkAth);
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
        // Single "Text" button (above the 15m RTH panel, same convention as H-Line) arms
        // text-placement mode on all 3 panels at once — no mirroring, each panel only places text
        // where IT was clicked. Reads the Windows clipboard fresh each time this button is pressed.
        // Source text for the "Text" tool below — declared here (used by btnText.Click) but only
        // actually added to the options grid layout further down, where optionsGridHost is built.
        var txtChartText = new TextBox
        {
            Dock       = DockStyle.Bottom,
            Height     = 80,
            Multiline  = true,
            ScrollBars = ScrollBars.Vertical,
            Font       = new Font("Segoe UI", 9F)
        };

        var btnText = new Button
        {
            Text     = "Text",
            Location = new Point(198, 4),
            Size     = new Size(60, 24)
        };
        btnRthTLine.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            var on = await rthPanel.ToggleTLineModeAsync();
            btnRthTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        if (rthPanel != null) rthPanel.OnTLinePlacedEvent += () => btnRthTLine.BackColor = SystemColors.Control;
        // Single H-Line button (above the 15m RTH panel) arms drawing on ALL 3 panels at once —
        // the user can then click on whichever one they want and it mirrors onto the other 2 (see
        // the OnHLineDrawnEvent wiring below), instead of needing a separate toggle per panel.
        btnRthHLine.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleHLineModeAsync();
            if (rthPanel != null) on = await rthPanel.ToggleHLineModeAsync();
            if (overnightPanel != null) on = await overnightPanel.ToggleHLineModeAsync();
            btnRthHLine.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };
        btnRthClear.Click += async (s, e) =>
        {
            if (rthPanel == null) return;
            await rthPanel.ClearDrawingsAsync();
            btnRthTLine.BackColor = SystemColors.Control;
            btnRthHLine.BackColor = SystemColors.Control;
            btnText.BackColor = SystemColors.Control;
        };
        btnText.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleTextModeAsync(txtChartText.Text);
            if (rthPanel != null) on = await rthPanel.ToggleTextModeAsync(txtChartText.Text);
            if (overnightPanel != null) on = await overnightPanel.ToggleTextModeAsync(txtChartText.Text);
            btnText.BackColor = on ? Color.LightBlue : SystemColors.Control;
        };

        // A click on whichever panel actually placed the text auto-disarms just THAT panel's own
        // JS state (see chart.html's textArmed handling) — the other 2 armed it too (btnText arms
        // all 3 at once) and are still waiting for a click of their own. Force them off too and
        // reset the button, so "one placement anywhere" fully normalizes the tool everywhere.
        void OnTextPlaced(ChartPanel? placedOn)
        {
            btnText.BackColor = SystemColors.Control;
            // Disarm-only calls — the text argument is irrelevant here since these panels never
            // get clicked while armed from this path.
            if (hourlyPanel != null && hourlyPanel != placedOn) _ = hourlyPanel.ToggleTextModeAsync(string.Empty);
            if (rthPanel != null && rthPanel != placedOn) _ = rthPanel.ToggleTextModeAsync(string.Empty);
            if (overnightPanel != null && overnightPanel != placedOn) _ = overnightPanel.ToggleTextModeAsync(string.Empty);
        }
        if (hourlyPanel != null) hourlyPanel.OnTextPlacedEvent += () => OnTextPlaced(hourlyPanel);
        if (rthPanel != null) rthPanel.OnTextPlacedEvent += () => OnTextPlaced(rthPanel);
        if (overnightPanel != null) overnightPanel.OnTextPlacedEvent += () => OnTextPlaced(overnightPanel);

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
        rthToolsHost.Controls.Add(btnRthHLine);
        rthToolsHost.Controls.Add(btnRthClear);
        rthToolsHost.Controls.Add(btnText);
        rthToolsHost.Controls.Add(btnBringAllForward);
        rthToolsHost.Controls.Add(chkBollingerEdges);
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
        // Per-symbol "send trade data to API/S3" toggle — when off, a trade opened from this
        // window's own options grid (colStrikeLive click) still opens normally in the grid/log,
        // but SaveTradeToApiAsync skips the POST (falls back to a local negative id, same
        // mechanism as an unreachable API), which UploadScreenshotAsync and the close Telegram
        // push already treat as "keep this trade fully local" — see those in Form1.cs. Persisted
        // per ticker (tickers.json), same pattern as chkTelegramEvents below.
        var chkAws = new CheckBox
        {
            Text     = "AWS",
            Location = new Point(236, 8),
            AutoSize = true,
            Checked  = Form1.IsAwsEnabledFor(_symbol)
        };
        chkAws.CheckedChanged += (s, e) => Form1.SetAwsEnabledFor(_symbol, chkAws.Checked);

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

        // Stops the auto-push-on-every-closed-candle Telegram loop that starts automatically once
        // a Demand/Supply zone rebote confirms (see ChartPanel.OnAutoZonePushTickEvent) — this is
        // the ONLY way to stop it; a future rebote (a different zone) re-arms it again.
        var btnStopPush = new Button
        {
            Text     = "Stop Push",
            Location = new Point(152, 30),
            Size     = new Size(80, 24)
        };

        // Per-ticker "send events to Telegram" toggle — gates only the event pushes (Piso/Techo,
        // T-Line, Abriendo la Volatilidad, Demand/Supply Zone, auto-push); trade open/close pushes
        // are untouched. Persisted per symbol in tickers.json via Form1.SetTelegramEnabledFor.
        var chkTelegramEvents = new CheckBox
        {
            Text     = "Telegram",
            Location = new Point(236, 34),
            AutoSize = true,
            Checked  = Form1.IsTelegramEnabledFor(_symbol)
        };
        chkTelegramEvents.CheckedChanged += (s, e) => Form1.SetTelegramEnabledFor(_symbol, chkTelegramEvents.Checked);

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
            btnRthHLine.BackColor = SystemColors.Control;
        };
        btn5Min.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var is5Min = await overnightPanel.ToggleIntervalAsync();
            btn5Min.BackColor = is5Min ? Color.LightBlue : SystemColors.Control;
        };
        // Single Arrow button (over panel 3, same convention as H-Line/Text) arms drawing on ALL
        // 3 panels at once — used to be overnightPanel-only. No mirroring: each panel only draws
        // where it was itself clicked, same as Text.
        btnArrow.Click += async (s, e) =>
        {
            bool on = false;
            if (hourlyPanel != null) on = await hourlyPanel.ToggleArrowModeAsync();
            if (rthPanel != null) on = await rthPanel.ToggleArrowModeAsync();
            if (overnightPanel != null) on = await overnightPanel.ToggleArrowModeAsync();
            btnArrow.BackColor = on ? Color.LightYellow : SystemColors.Control;
        };
        btnStopPush.Click += (s, e) => overnightPanel?.StopAutoZonePush();

        toolsHost.Controls.Add(btnDzSz);
        toolsHost.Controls.Add(btnRect);
        toolsHost.Controls.Add(chkAws);
        toolsHost.Controls.Add(btnClear);
        toolsHost.Controls.Add(btn5Min);
        toolsHost.Controls.Add(btnArrow);
        toolsHost.Controls.Add(btnStopPush);
        toolsHost.Controls.Add(chkTelegramEvents);
        toolsHost.Controls.Add(lblLiveTick);
        toolbar.Controls.Add(toolsHost, 2, 0);

        // Small event log below the charts — logs Cross-SMA cruce/rebote detections (so the
        // Telegram-push feature can be sanity-checked without digging through Telegram itself).
        // Temporary/diagnostic for now.
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
            // T-Line + SMA20 breakout — pushes the combined 3-chart image, same as a trade close.
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

        if (rthPanel != null)
        {
            // Panel 2's own T-Lines are independent from panel 1's — same breakout signal,
            // evaluated against panel 2's own SMA20/candles, logged/pushed identically.
            rthPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
                _ = SendTLineSignalTelegramPushAsync(message);
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

            // Supply Zone rebote — symmetric counterpart, same self-contained pattern.
            overnightPanel.OnSupplyZoneReboundEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // Auto-push loop armed by either rebote above — fires on EVERY closed 15m candle from
            // then on, pushing the combined 3-chart snapshot each time, until "Stop Push" is
            // clicked (see btnStopPush) or a fresh rebote later re-arms it.
            overnightPanel.OnAutoZonePushTickEvent += candle => _ = SendAutoZonePushAsync(candle);
        }

        // "Abriendo la Volatilidad": when the 1h panel resolves a Piso/Techo watch (any SMA
        // period), arm the 15m RTH panel's Bollinger-widening watch in the direction the
        // resolution implies price is now headed. Cruce en Techo (breaks up through resistance)
        // and Rebote en Piso (bounces up off support) are both bullish/CALL (upper band). Cruce
        // en Piso (breaks down through support) and Rebote en Techo (rejected down off
        // resistance) are both bearish/PUT (lower band) — see
        // ChartPanel.ArmVolatilityOpeningWatch/EvaluateVolatilityOpening. caption already carries
        // the "evaluando Abriendo la Volatilidad (Alza/Baja)" suffix (ChartPanel.
        // AppendVolatilityArmSuffix) — baked in at the source instead of appended here, so it can't
        // be silently dropped for any resolution path (close-based, live gap-cross, etc).
        if (hourlyPanel != null && rthPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                var bullish = pisoTecho == "Techo" ? evento == "Cruce" : evento == "Rebote";
                rthPanel.ArmVolatilityOpeningWatch(bullish);

                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
                _ = SendPisoTechoTelegramPushAsync(caption);
            };
        }
        else if (hourlyPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
                _ = SendPisoTechoTelegramPushAsync(caption);
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnVolatilityOpeningEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // "Ya abiertas al armar" — informational heads-up, log-only, doesn't wait for the
            // spot to actually touch a band (see ChartPanel.OnVolatilityAlreadyOpenEvent).
            rthPanel.OnVolatilityAlreadyOpenEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        // "Abriendo Bollinger con Volatilidad" — logs the exact moment "BB" starts showing on
        // EITHER panel (1h or 15m RTH), already persisted to EventLogStore by ChartPanel itself
        // (see OnBollingerOpeningEvent). Here: mirror into crossLog with a timestamp for real-time
        // visibility, AND save the combined 3-chart screenshot into the events .md — no Telegram
        // push for this one, just the local record (see SaveBollingerOpeningSnapshotAsync).
        if (hourlyPanel != null)
        {
            hourlyPanel.OnBollingerOpeningEvent += caption =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  [1h] {caption}{Environment.NewLine}"));
                _ = SaveBollingerOpeningSnapshotAsync(caption);
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnBollingerOpeningEvent += caption =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  [15m RTH] {caption}{Environment.NewLine}"));
                _ = SaveBollingerOpeningSnapshotAsync(caption);
            };
        }

        // "Expuesto en 3 charts" — premarket-only: on every premarket tick (fired from the 1h
        // panel, see ChartPanel.OnPreMarketPriceUpdated), check whether that price broke the SAME
        // side (upper or lower) of the Bollinger(20,2) band on Daily, 1h AND 15m RTH all at once.
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

        // Piso/Techo reference line: mirrors each armed SMA's pre-market level onto BOTH the 15m
        // RTH and RTH+Overnight panels (dashed, same color as that SMA on the 1h panel) — visual
        // reference for "how far price could go and bounce" without needing the 1h panel open.
        // Removed automatically if the market-open gap later invalidates that SMA.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnPisoTechoLevelReadyEvent += (period, price) =>
            {
                // BeginInvoke — this event can fire from Streamer_OnNewCandle's background
                // (WebSocket) thread, and a direct ExecuteScriptAsync call from that thread
                // silently fails (same threading bug the PM indicator had).
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    var sessionEnd   = GetTodaySessionEndFakeEpoch();
                    if (rthPanel != null) _ = rthPanel.MarkPisoTechoRefLineAsync(period, price, sessionStart, sessionEnd);
                    if (overnightPanel != null) _ = overnightPanel.MarkPisoTechoRefLineAsync(period, price, sessionStart, sessionEnd);
                });
            };
            hourlyPanel.OnPisoTechoLevelRemovedEvent += period =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (rthPanel != null) _ = rthPanel.RemovePisoTechoRefLineAsync(period);
                    if (overnightPanel != null) _ = overnightPanel.RemovePisoTechoRefLineAsync(period);
                });
            };

            // Race fix: hourlyPanel's HandleCreated (added to the layout earlier in this
            // constructor) can fire EvaluatePisoTechoOnce — and this very event — before the
            // subscription above ever runs, especially when its history loads fast (e.g. plenty of
            // HourlyCandleStore data already cached locally). Catch up immediately in that case.
            hourlyPanel.ReplayPisoTechoLevels();
        }

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
                    crossLog.AppendText($"{DateTime.Now:HH:mm:ss}  PM + BB alineados en {direction} (1h y 15m RTH){Environment.NewLine}");
                }
                pmBbAligned = aligned;
            }

            hourlyPanel.OnPuntoMedioLevelEvent += _ => CheckPmBbAlignment();
            rthPanel.OnPuntoMedioLevelEvent += _ => CheckPmBbAlignment();
            hourlyPanel.OnBollingerWideningLevelEvent += (show, bullish) => { hourlyBbBullish = show ? bullish : null; CheckPmBbAlignment(); };
            rthPanel.OnBollingerWideningLevelEvent += (show, bullish) => { rthBbBullish = show ? bullish : null; CheckPmBbAlignment(); };
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
            panel.OnPrevDayHiLoDebugEvent += detail =>
            {
                // Diagnostic-only — written to disk instead of crossLog so it doesn't clutter the
                // visible UI now that the underlying "H-Lines only on one panel" bug is confirmed
                // fixed; kept on disk for later review instead of being thrown away outright.
                try
                {
                    Directory.CreateDirectory(@"C:\OptionsData\EventLog");
                    File.AppendAllText(@"C:\OptionsData\EventLog\prevday_hilo_debug.log",
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {detail}{Environment.NewLine}");
                }
                catch { /* best-effort diagnostic logging */ }
            };
        }

        // Stk line delete: markStrike draws the same green line on all 3 panels at trade open —
        // deleting it (click + Delete) on any ONE panel removes it from the other 2 as well, so
        // the user doesn't have to repeat the same delete 3 times.
        var allChartPanels = new[] { hourlyPanel, rthPanel, overnightPanel };
        foreach (var panel in allChartPanels)
        {
            if (panel == null) continue;
            panel.OnStrikeDeletedEvent += price =>
            {
                foreach (var sibling in allChartPanels)
                {
                    if (sibling != null && sibling != panel) _ = sibling.RemoveStrikeLineAsync(price);
                }
            };
        }

        // All-Time High: the 1h panel is the only one that persists a new value (at the RTH close,
        // see ChartPanel.EvaluateAllTimeHighAtClose) — mirror it onto the other 2 panels' reference
        // lines so all 3 stay in sync for the rest of the day (and on the next chart open, each
        // panel loads it fresh from AllTimeHighStore on its own anyway).
        if (hourlyPanel != null)
        {
            hourlyPanel.OnAllTimeHighUpdatedEvent += newValue =>
            {
                if (rthPanel != null) _ = rthPanel.MarkAllTimeHighAsync(newValue);
                if (overnightPanel != null) _ = overnightPanel.MarkAllTimeHighAsync(newValue);
            };
        }

        // H-Line delete: same idea as the Stk line above — manually-drawn H-Lines and the
        // auto-drawn prev-day High/Low both get mirrored (by price) onto the other 2 panels when
        // deleted (click + Delete) from any one of them.
        foreach (var panel in allChartPanels)
        {
            if (panel == null) continue;
            panel.OnHLineDeletedEvent += price =>
            {
                foreach (var sibling in allChartPanels)
                {
                    if (sibling != null && sibling != panel) _ = sibling.RemoveHLineAsync(price);
                }
            };
        }

        // H-Line draw: drawing one on ANY of the 3 panels (the single shared H-Line button arms
        // all 3 at once — see btnRthHLine above) mirrors it onto the other 2, same idea as T-Line.
        foreach (var panel in allChartPanels)
        {
            if (panel == null) continue;
            panel.OnHLineDrawnEvent += (time, price) =>
            {
                foreach (var sibling in allChartPanels)
                {
                    if (sibling != null && sibling != panel) _ = sibling.AddMirroredHLineAsync(time, price);
                }
            };
        }

        // Live options grid — same 7 fields as Form1's dgvQuotes but ONE column set shared by
        // both Calls and Puts (one row per option) instead of a call-side/put-side pair, and
        // Bid/Ask/Sprd/Conts/Level moved to the right of Strike with Range pushed to the end
        // (Form1's own grid keeps Range/Sprd/Bid/Ask BEFORE the Strike button — different order,
        // per explicit request for this one).
        _dgvOptions.Columns.AddRange(
            new DataGridViewButtonColumn { Name = "colStrikeLive", HeaderText = "Strike", Width = 46, FlatStyle = FlatStyle.Standard, UseColumnTextForButtonValue = false },
            new DataGridViewTextBoxColumn { Name = "colBidLive",   HeaderText = "Bid",   Width = 38, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colAskLive",   HeaderText = "Ask",   Width = 38, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colSprdLive",  HeaderText = "Sprd",  Width = 34, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colContsLive", HeaderText = "Conts", Width = 40, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colLevelLive", HeaderText = "Level", Width = 38, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "colRangeLive", HeaderText = "Range", Width = 70, ReadOnly = true });
        foreach (DataGridViewColumn col in _dgvOptions.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Same per-cell styling as Form1's own dgvQuotes (DgvQuotes_CellFormatting): Sprd bold red,
        // Ask bold dark green, Bid background green when that row's Sprd <= 2.
        _dgvOptions.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _dgvOptions.Rows[e.RowIndex];
            var sprdCol  = _dgvOptions.Columns["colSprdLive"]!.Index;
            var bidCol   = _dgvOptions.Columns["colBidLive"]!.Index;
            var askCol   = _dgvOptions.Columns["colAskLive"]!.Index;
            var rangeCol = _dgvOptions.Columns["colRangeLive"]!.Index;

            if (e.ColumnIndex == sprdCol)
            {
                e.CellStyle.ForeColor = Color.Red;
                e.CellStyle.Font = new Font(_dgvOptions.Font, FontStyle.Bold);
            }
            else if (e.ColumnIndex == askCol)
            {
                e.CellStyle.ForeColor = Color.DarkGreen;
                e.CellStyle.Font = new Font(_dgvOptions.Font, FontStyle.Bold);
            }
            else if (e.ColumnIndex == bidCol)
            {
                e.CellStyle.BackColor = decimal.TryParse(row.Cells["colSprdLive"].Value?.ToString(), out var sprd) && sprd <= 2
                    ? Color.LightGreen
                    : _dgvOptions.DefaultCellStyle.BackColor;
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
                e.CellStyle.BackColor = inRange ? Color.LightGreen : _dgvOptions.DefaultCellStyle.BackColor;
            }
        };

        // Strike button: dark green for Call rows, red for Put rows, light gray when blocked —
        // identical to DgvQuotes_CellPainting's colStrikePrice button on Form1's own grid (same
        // IsRowTradeBlocked rule: bid == 0, OR spread >= 6, OR 0 contracts).
        _dgvOptions.CellPainting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvOptions.Columns["colStrikeLive"]!.Index) return;

            var val     = e.Value?.ToString();
            var row     = _dgvOptions.Rows[e.RowIndex];
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
            using var textFont  = new Font(_dgvOptions.Font, FontStyle.Bold);
            e.Graphics!.FillRectangle(fillBrush, btnRect);
            e.Graphics.DrawRectangle(borderPen, btnRect);
            TextRenderer.DrawText(e.Graphics, val, textFont, btnRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            e.Handled = true;
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
        // txtChartText (source text for the "Text" tool, replaces the Windows clipboard entirely
        // per explicit request) declared earlier alongside btnText — just wire it into the layout
        // here. The grid (Dock=Fill) shrinks automatically to make room since it's added after.
        optionsGridHost.Controls.Add(_dgvOptions);
        optionsGridHost.Controls.Add(txtChartText);
        optionsGridHost.Controls.Add(lblExpDate);

        void RefreshOptionsGrid()
        {
            var snapshot = _form1.GetQuoteSnapshot(_symbol);
            if (snapshot == null) return;
            if (_dgvOptions.IsDisposed || !_dgvOptions.IsHandleCreated) return;
            _dgvOptions.BeginInvoke(() => Form1.PopulateSingleSideOptionsGrid(
                _dgvOptions, snapshot.Value.AllQuotes, snapshot.Value.OtmCalls, snapshot.Value.OtmPuts, snapshot.Value.Ticker));
            if (!lblExpDate.IsDisposed)
                lblExpDate.Text = $"ExpDate: {ExpirationDateResolver.Resolve(snapshot.Value.Ticker.ExpDate):yyyy-MM-dd}";
        }

        // Strike click: forwards into Form1's own click handler — opens a trade using whatever
        // radio mode (No Trade / No Trade-Target / Trade / Trade-Target) is currently selected on
        // Form1, identical behavior to clicking Strike there directly.
        _dgvOptions.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvOptions.Columns["colStrikeLive"]!.Index) return;
            var row = _dgvOptions.Rows[e.RowIndex];
            var rowType = row.Tag?.ToString();
            var strikeText = row.Cells["colStrikeLive"].Value?.ToString();
            if (string.IsNullOrEmpty(rowType) || string.IsNullOrEmpty(strikeText)) return;
            _form1.TriggerQuoteStrikeClick(_symbol, rowType, strikeText, chkAws.Checked);
        };

        _form1.OnQuotesUpdatedEvent += OnForm1QuotesUpdated;
        void OnForm1QuotesUpdated(string updatedSymbol)
        {
            if (updatedSymbol == _symbol) RefreshOptionsGrid();
        }
        FormClosed += (s, e) => _form1.OnQuotesUpdatedEvent -= OnForm1QuotesUpdated;
        Load += (s, e) => RefreshOptionsGrid();

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
        FormClosed += (s, e) => _form1.OnTradesUpdatedEvent -= OnForm1TradesUpdated;
        Load += (s, e) => RefreshTradesGrid();

        Controls.Add(layout);
        Controls.Add(toolbar);
        Controls.Add(tradesGridHost);
        Controls.Add(crossLog);
        Controls.Add(optionsGridHost);
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

    // Green "Stk=xxx" line — panel 3 (15m RTH+Overnight) only, per explicit request. Fired when a
    // trade (demo or real) opens.
    public Task MarkStrikeOnOvernightChartAsync(decimal strike) =>
        _overnightPanel?.MarkStrikeAsync(strike) ?? Task.CompletedTask;

    // White spot-price line — panels 2 (15m RTH) and 3 (15m RTH+Overnight), same marker the
    // Simulator already draws on trade open/close. Fired at both. Originally panel 3 only; panel 2
    // added per explicit request.
    public async Task MarkEntrySpotOnOvernightChartAsync(decimal price)
    {
        if (_rthPanel != null) await _rthPanel.MarkEntrySpotAsync(price);
        if (_overnightPanel != null) await _overnightPanel.MarkEntrySpotAsync(price);
    }

    // Today's 9:30 AM ET, in the same "ET wall-clock digits disguised as UTC" fake-epoch units the
    // chart itself uses (ChartPanel.FakeUtcEpochSeconds) — the Piso/Techo reference line's anchor,
    // computed independently of any candle data so it can't race against history loading (see
    // markPisoTechoRefLine in chart.html for the full rationale).
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // Yesterday's last hourly candle (its close) — the Piso/Techo reference line's real anchor,
    // per explicit request ("así estaba implementado"): it should be born at yesterday's close and
    // run through the whole of today's RTH session, not at a fixed hour of today. Read from
    // HourlyCandleStore (already persisted/updated by the 1h panel) instead of the 1h panel's own
    // in-memory candles, so this can't race against that panel's history load. Falls back to
    // today 4:00 AM ET if no prior-day history is on disk yet (shouldn't normally happen).
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

    // RTH session close (16:00 ET, today) — so the Piso/Techo reference line stops there instead
    // of running off to the chart's own right edge.
    private static long GetTodaySessionEndFakeEpoch()
    {
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).Date;
        var sessionEndEastern = todayEastern.AddHours(16);
        var sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionEndEastern, DateTimeKind.Unspecified), EasternZone);
        return ChartPanel.FakeUtcEpochSeconds(sessionEndUtc);
    }

    // Pushes the combined 3-chart snapshot to Telegram for the T-Line+SMA20 breakout signal —
    // best-effort, same as every other Telegram push in this app: a failure here must never
    // affect the chart/detection logic itself.
    private async Task SendTLineSignalTelegramPushAsync(string caption)
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

    // Pushes the combined 3-chart snapshot for a Piso/Techo Cruce/Rebote resolution — additional to
    // ChartPanel's own single-panel push (SendChartToTelegramAsync, fired from the 1h panel itself
    // for the SAME event), per explicit request. Same best-effort pattern as every other push here.
    // Saves the combined 3-chart screenshot for "Abriendo Bollinger con Volatilidad" into the
    // events .md — no Telegram push for this one (per explicit request, it's local-only), just
    // the same capture-and-embed step every other combined-snapshot event already does.
    private async Task SaveBollingerOpeningSnapshotAsync(string caption)
    {
        try
        {
            using var combined = await CaptureCombinedChartImageAsync();
            if (combined == null) return;

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_BollingerOpening_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
        }
        catch
        {
            // Best-effort, same as every other snapshot/push in this file — never let this affect
            // the analysis that just fired.
        }
    }

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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
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

    // Pushes the combined 3-chart snapshot on every closed 15m candle while the auto-push loop is
    // armed (see ChartPanel.OnAutoZonePushTickEvent) — a Demand/Supply zone rebote confirmed and
    // "Stop Push" hasn't been clicked since. Same best-effort pattern as every other Telegram push.
    private async Task SendAutoZonePushAsync(CandleData candle)
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
                return;
            }

            var caption = $"Auto-push Rebote — Close {candle.Close:F2}";

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_AutoZonePush_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "AutoZonePush", DateTime.Now));
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
