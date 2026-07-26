using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Single window hosting the 3 live-chart panels (1h / 15m RTH / 15m RTH+Overnight) side by side
// horizontally — each panel has its own WebView2 + independent Schwab streaming connection.
public class MultiChartForm : Form
{
    public MultiChartForm(string symbol, Func<SchwabStreamerClient> createStreamer)
    {
        Text          = $"Live Charts — {symbol}";
        Width         = 1740;
        Height        = 640;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
            BackColor   = Color.FromArgb(19, 23, 34)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var modes = new[] { ChartPanelMode.Hourly15, ChartPanelMode.Fifteen_RTH, ChartPanelMode.Fifteen_Full };
        for (int i = 0; i < modes.Length; i++)
        {
            var panel = new ChartPanel(symbol, createStreamer(), modes[i])
            {
                Dock    = DockStyle.Fill,
                Margin  = new Padding(i == 0 ? 0 : 2, 0, i == modes.Length - 1 ? 0 : 2, 0)
            };
            layout.Controls.Add(panel, i, 0);
        }

        Controls.Add(layout);
    }
}
