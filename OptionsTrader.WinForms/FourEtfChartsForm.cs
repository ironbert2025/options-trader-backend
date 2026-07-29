using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Single window showing the 1h chart for the 4 major index ETFs side by side, fed by the same
// shared streamer as the per-ticker Live Chart windows (MultiChartForm) — no separate connection.
public class FourEtfChartsForm : Form
{
    // Hardcoded for now per explicit request ("por ahora a mano") — SPY/QQQ already come from
    // TickerSettingsStore, but DIA/IWM don't yet. Later this should come from the tickers table
    // instead of being fixed here.
    public static readonly string[] Symbols = { "SPY", "QQQ", "DIA", "IWM" };

    public FourEtfChartsForm(SchwabStreamerClient historyClient, ICandleFeed liveFeed)
    {
        Text          = "1h Charts — SPY / QQQ / DIA / IWM";
        Width         = 1040;
        Height        = 360;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = Symbols.Length,
            RowCount    = 1,
            Padding     = new Padding(6)
        };
        foreach (var _ in Symbols)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Symbols.Length));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        for (int i = 0; i < Symbols.Length; i++)
        {
            var panel = new ChartPanel(Symbols[i], historyClient, liveFeed, ChartPanelMode.Hourly15)
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6)
            };
            layout.Controls.Add(panel, i, 0);
        }

        Controls.Add(layout);
    }
}
