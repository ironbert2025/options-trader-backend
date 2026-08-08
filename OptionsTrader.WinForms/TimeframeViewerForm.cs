using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Read-only "price action from several perspectives" viewer for ONE symbol at a time — a 2x2 grid
// of TimeframeChartPanel (5m / 15m / 1h / 4h, all RTH+Overnight), fed by the same shared live
// streamer as the per-ticker Live Chart windows. No drawing tools, no auto-detection/Telegram —
// just candles, for quickly comparing how price looks across timeframes.
public class TimeframeViewerForm : Form
{
    // Order matches grid position (row-major: top-left, top-right, bottom-left, bottom-right).
    private static readonly (string Label, int IntervalMinutes)[] Timeframes =
    {
        ("4h", 240), ("5m", 5), ("1h", 60), ("15m", 15)
    };

    private readonly ComboBox _cmbSymbol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(8, 8), Size = new Size(120, 24) };
    private readonly Button _btnCargar   = new() { Text = "Cargar", Location = new Point(136, 8), Size = new Size(70, 24) };

    private readonly TableLayoutPanel _chartsHost = new()
    {
        Location = new Point(8, 40), Size = new Size(1296, 907), // 90% of 1440x1008
        ColumnCount = 2, RowCount = 2
    };

    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly List<TimeframeChartPanel> _panels = new();

    public TimeframeViewerForm(SchwabStreamerClient historyClient, ICandleFeed liveFeed)
    {
        _historyClient = historyClient;
        _liveFeed      = liveFeed;

        Text          = "Multi-Timeframe Viewer";
        Width         = 1323;
        Height        = 990;
        StartPosition = FormStartPosition.CenterScreen;

        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        Controls.Add(_cmbSymbol);
        Controls.Add(_btnCargar);
        Controls.Add(_chartsHost);

        _btnCargar.Click += (s, e) => LoadSelectedSymbol();

        Load += (s, e) => LoadSymbols();
    }

    private void LoadSymbols()
    {
        var symbols = TickerSettingsStore.Load().Select(t => t.Symbol).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        _cmbSymbol.Items.Clear();
        foreach (var s in symbols) _cmbSymbol.Items.Add(s);
        if (_cmbSymbol.Items.Count > 0)
        {
            _cmbSymbol.SelectedIndex = 0;
            LoadSelectedSymbol();
        }
    }

    // ChartPanel/TimeframeChartPanel have no "change symbol" support anywhere in this codebase —
    // every existing multi-chart window (MultiChartForm, FourEtfChartsForm) always builds fresh
    // panels per symbol at construction. Same here: tear down the 4 panels and build new ones.
    private void LoadSelectedSymbol()
    {
        if (_cmbSymbol.SelectedItem is not string symbol) return;

        _chartsHost.Controls.Clear();
        foreach (var panel in _panels) panel.Dispose();
        _panels.Clear();

        for (int i = 0; i < Timeframes.Length; i++)
        {
            var (label, minutes) = Timeframes[i];
            var panel = new TimeframeChartPanel(symbol, _historyClient, _liveFeed, minutes, label)
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6, 2, 6, 6)
            };
            _panels.Add(panel);
            _chartsHost.Controls.Add(panel, i % 2, i / 2);
        }
    }
}
