using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Single window hosting the 3 live-chart panels (1h / 15m RTH / 15m RTH+Overnight) side by side
// horizontally. All 3 share ONE Schwab streaming connection/subscription — Schwab's CHART_EQUITY
// service only ever pushes 1-minute candles regardless of the interval you display, so each
// panel just aggregates the same incoming ticks into its own bucket size independently.
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
        Height        = 660;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control; // visible in the gaps between/around the 3 panels

        // Toolbar strip on top — same 3-column layout as the charts below, so each column's
        // controls line up with the chart panel directly beneath it.
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 36,
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

        // Push: captures the 1h chart (WebView2 native preview capture) and sends it to the
        // configured Telegram channel. Lives in the toolbar column above that panel (column 0).
        var btnPush = new Button
        {
            Text     = "Push",
            Anchor   = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(0, 4),
            Size     = new Size(70, 24)
        };
        btnPush.Click += async (s, e) =>
        {
            if (hourlyPanel == null) return;
            btnPush.Enabled = false;
            try
            {
                var (ok, detail) = await hourlyPanel.PushToTelegramAsync();
                MessageBox.Show(ok ? "Chart enviado a Telegram." : $"Falló el envío:\n{detail}",
                    "Push — Telegram", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
            finally
            {
                btnPush.Enabled = true;
            }
        };
        toolbar.Controls.Add(btnPush, 0, 0);

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

        FormClosing += async (s, e) => await _streamer.DisposeAsync();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Connect + subscribe ONCE for all 3 panels — their OnNewCandle handlers are already
        // wired up (each ChartPanel subscribes in its own constructor, which already ran).
        try
        {
            await _streamer.ConnectAsync();
            await _streamer.SubscribeChartEquity(_symbol);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start live streaming for {_symbol}:\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
