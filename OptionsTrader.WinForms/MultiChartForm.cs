using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Single window (one per ticker) hosting the 3 live-chart panels (1h / 15m RTH / 15m
// RTH+Overnight) side by side horizontally. The SchwabStreamerClient passed in is shared across
// EVERY open ticker window in the app — Form1 owns connecting it once and subscribing it to all
// configured tickers (Schwab allows only one streaming connection per account); each ChartPanel
// filters the shared OnNewCandle stream down to its own symbol before aggregating.
public class MultiChartForm : Form
{
    private readonly SchwabStreamerClient _streamer;
    private readonly string _symbol;

    public MultiChartForm(string symbol, SchwabStreamerClient streamer)
    {
        _symbol   = symbol;
        _streamer = streamer;

        Text          = $"Live Charts — {symbol}";
        Width         = 1740;
        Height        = 714;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control; // visible in the gaps between/around the 3 panels

        // Toolbar strip on top — same 3-column layout as the charts below, so each column's
        // controls line up with the chart panel directly beneath it.
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 90,
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
            Padding     = new Padding(6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ChartPanel? overnightPanel = null;
        ChartPanel? hourlyPanel = null;
        var modes = new[] { ChartPanelMode.Hourly15, ChartPanelMode.Fifteen_RTH, ChartPanelMode.Fifteen_Full };
        for (int i = 0; i < modes.Length; i++)
        {
            // All 3 panels get the SAME streamer instance — they only ever read its events /
            // call its (stateless) REST history method, never each other's connection state.
            var panel = new ChartPanel(symbol, _streamer, modes[i])
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6)
            };
            layout.Controls.Add(panel, i, 0);
            if (modes[i] == ChartPanelMode.Fifteen_Full) overnightPanel = panel;
            if (modes[i] == ChartPanelMode.Hourly15) hourlyPanel = panel;
        }

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
        // Cross-SMA toggles, one on each row so they line up visually with that grid.
        var btnTLine = new Button
        {
            Text     = "T-Line",
            Location = new Point(periods.Length * 42 + 6, 2),
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
            Location = new Point(periods.Length * 42 + 6, 30),
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
            Location = new Point(periods.Length * 42 + 6, 58),
            Size     = new Size(60, 24)
        };
        btnHourlyClear.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            await hourlyPanel.ClearDrawingsAsync();
            btnTLine.BackColor = SystemColors.Control;
            btnHLine.BackColor = SystemColors.Control;
        };

        crossHost.Controls.Add(btnTLine);
        crossHost.Controls.Add(btnHLine);
        crossHost.Controls.Add(btnHourlyClear);
        toolbar.Controls.Add(crossHost, 0, 0);

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
        };

        toolsHost.Controls.Add(btnDzSz);
        toolsHost.Controls.Add(btnRect);
        toolsHost.Controls.Add(btnClear);
        toolbar.Controls.Add(toolsHost, 2, 0);

        Controls.Add(layout);
        Controls.Add(toolbar);

        // The streamer is shared across every open ticker window (Schwab allows only one
        // streaming connection per account) — Form1 owns connecting/subscribing/disposing it
        // for the app's whole lifetime, not this window.
    }
}
