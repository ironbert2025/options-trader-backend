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

    public MultiChartForm(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;

        Text          = $"Live Charts — {symbol}";
        Width         = 900;
        Height        = 440;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control; // visible in the gaps between/around the 3 panels

        // Toolbar strip on top — same 3-column layout as the charts below, so each column's
        // controls line up with the chart panel directly beneath it.
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 110,
            ColumnCount = 3,
            RowCount    = 1,
            Padding     = new Padding(6, 4, 6, 0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
            Padding     = new Padding(6, 2, 6, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
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

        // Cross-SMA monitors: 8 small toggles (UP/DOWN x 20/40/100/200), 2 rows x 4 columns, in
        // the toolbar column above the 1h panel (column 0). While armed, each one pushes the 1h
        // chart to Telegram the moment a candle closes with a genuine crossover of that SMA.
        var crossHost = new Panel { Dock = DockStyle.Fill };
        var periods = new[] { 20, 40, 100, 200 };
        for (int col = 0; col < periods.Length; col++)
        {
            var period = periods[col];

            var btnUp = new Button
            {
                Text     = $"↑{period}",
                Location = new Point(col * 42, 2),
                Size     = new Size(40, 24)
            };
            btnUp.Click += (s, e) =>
            {
                if (hourlyPanel == null) return;
                var on = hourlyPanel.ToggleCrossMonitor(period, up: true);
                btnUp.BackColor = on ? Color.LightGreen : SystemColors.Control;
            };

            var btnDown = new Button
            {
                Text     = $"↓{period}",
                Location = new Point(col * 42, 30),
                Size     = new Size(40, 24)
            };
            btnDown.Click += (s, e) =>
            {
                if (hourlyPanel == null) return;
                var on = hourlyPanel.ToggleCrossMonitor(period, up: false);
                btnDown.BackColor = on ? Color.LightSalmon : SystemColors.Control;
            };

            crossHost.Controls.Add(btnUp);
            crossHost.Controls.Add(btnDown);
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
            Location = new Point(toolsStartX, 30),
            Size     = new Size(60, 24)
        };

        // Filled gray rectangle marking sideways/consolidation ranges around price+SMAs. Click
        // its border to select (yellow outline), then press Delete to remove just that one.
        var btnRectGris = new Button
        {
            Text     = "Rect",
            Location = new Point(toolsStartX + 66, 30),
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
            Location = new Point(toolsStartX, 56),
            Size     = new Size(60, 24)
        };
        var btnTecho = new Button
        {
            Text     = "Techo",
            Location = new Point(toolsStartX + 66, 56),
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
            Location = new Point(toolsStartX, 82),
            Size     = new Size(60, 24)
        };
        var btnFlechaRoja = new Button
        {
            Text     = "↓ Roja",
            Location = new Point(toolsStartX + 66, 82),
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
        rthToolsHost.Controls.Add(btnRthTLine);
        rthToolsHost.Controls.Add(btnRthHLine);
        rthToolsHost.Controls.Add(btnRthClear);
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
            Location  = new Point(0, 58),
            Size      = new Size(146, 20),
            Font      = new Font("Consolas", 9F),
            ForeColor = Color.White
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

        Controls.Add(layout);
        Controls.Add(toolbar);

        // historyClient/liveFeed are owned by Form1 for the app's whole lifetime (connecting,
        // subscribing, and disposing them) — not this window.
    }

    // Renders the 3 charts (via WebView2, not a screen capture) and stitches them side by side in
    // the same left-to-right order they're shown on screen (1h, 15m RTH, 15m RTH+Overnight), for
    // Form1 to save as a single trade snapshot. Returns null if any panel isn't ready.
    public async Task<Bitmap?> CaptureCombinedChartImageAsync()
    {
        if (_hourlyPanel == null || _rthPanel == null || _overnightPanel == null) return null;

        var panels = new[] { _hourlyPanel, _rthPanel, _overnightPanel };
        var images = new Bitmap[panels.Length];
        try
        {
            for (int i = 0; i < panels.Length; i++)
                images[i] = await panels[i].CaptureImageAsync();

            var width  = images.Sum(img => img.Width);
            var height = images.Max(img => img.Height);
            var combined = new Bitmap(width, height);
            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(Color.Black);
                var x = 0;
                foreach (var img in images)
                {
                    g.DrawImage(img, x, 0);
                    x += img.Width;
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
