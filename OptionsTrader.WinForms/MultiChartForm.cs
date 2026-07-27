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
        }

        // DZ/SZ: click-to-draw Demand Zone (green) / Supply Zone (red) price lines — only on the
        // 15m RTH+Overnight panel, so the button sits in the toolbar column above that panel
        // (column index 2, matching Fifteen_Full's position in the layout below).
        var btnDzSz = new Button
        {
            Text   = "DZ/SZ",
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Size   = new Size(70, 24)
        };
        btnDzSz.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleDzSzModeAsync();
            btnDzSz.BackColor = on ? Color.LightGreen : SystemColors.Control;
        };
        toolbar.Controls.Add(btnDzSz, 2, 0);

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
