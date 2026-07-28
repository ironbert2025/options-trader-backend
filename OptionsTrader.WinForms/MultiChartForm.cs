using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

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
    private readonly IOtmTradeGateway _tradeGateway;
    private readonly string _symbol;

    // 6 Call + 6 Put buttons, closest-to-money first — created once, just updated in place on
    // every OtmOptionsUpdated tick instead of being recreated (less flicker).
    private readonly Button[] _callButtons = new Button[6];
    private readonly Button[] _putButtons = new Button[6];

    public MultiChartForm(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, IOtmTradeGateway tradeGateway)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _tradeGateway  = tradeGateway;

        Text          = $"Live Charts — {symbol}";
        Width         = 980;
        Height        = 420;
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

        // 4th column (fixed width) holds the OTM Call/Put buttons — part of THIS row so its
        // height always matches the 3 charts' height exactly, instead of a separate Dock=Right
        // panel that used to span the whole window including the toolbar area above.
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 4,
            RowCount    = 1,
            Padding     = new Padding(6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ChartPanel? overnightPanel = null;
        ChartPanel? hourlyPanel = null;
        var modes = new[] { ChartPanelMode.Hourly15, ChartPanelMode.Fifteen_RTH, ChartPanelMode.Fifteen_Full };
        for (int i = 0; i < modes.Length; i++)
        {
            // All 3 panels share the SAME historyClient/liveFeed — they only ever read events /
            // call the stateless REST history method, never each other's connection state.
            var panel = new ChartPanel(symbol, _historyClient, _liveFeed, modes[i])
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

        // OTM Call/Put buttons — native WinForms controls in their own column (added to `layout`
        // below, so their height matches the charts' height, not the whole window). 6 Call
        // buttons on top, 6 Put buttons below, closest-to-money first. AutoScroll is a safety net
        // in case the window is short enough that all 12 + labels don't fit.
        var otmPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoScroll = true };
        const int btnWidth = 96, btnHeight = 26, btnGap = 2;
        var y = 2;
        otmPanel.Controls.Add(new Label { Text = "CALL", Location = new Point(4, y), Size = new Size(btnWidth, 14), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.LimeGreen, Font = new Font(Font, FontStyle.Bold) });
        y += 16;
        for (int i = 0; i < _callButtons.Length; i++)
        {
            var btn = new Button { Location = new Point(4, y), Size = new Size(btnWidth, btnHeight), BackColor = Color.LightGreen, Visible = false };
            btn.Click += (s, e) => OnOtmButtonClick(btn);
            otmPanel.Controls.Add(btn);
            _callButtons[i] = btn;
            y += btnHeight + btnGap;
        }
        y += 6;
        otmPanel.Controls.Add(new Label { Text = "PUT", Location = new Point(4, y), Size = new Size(btnWidth, 14), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.OrangeRed, Font = new Font(Font, FontStyle.Bold) });
        y += 16;
        for (int i = 0; i < _putButtons.Length; i++)
        {
            var btn = new Button { Location = new Point(4, y), Size = new Size(btnWidth, btnHeight), BackColor = Color.LightSalmon, Visible = false };
            btn.Click += (s, e) => OnOtmButtonClick(btn);
            otmPanel.Controls.Add(btn);
            _putButtons[i] = btn;
            y += btnHeight + btnGap;
        }
        layout.Controls.Add(otmPanel, 3, 0);

        _tradeGateway.OtmOptionsUpdated += TradeGateway_OtmOptionsUpdated;
        FormClosed += (s, e) => _tradeGateway.OtmOptionsUpdated -= TradeGateway_OtmOptionsUpdated;

        Controls.Add(layout);
        Controls.Add(toolbar);

        // historyClient/liveFeed are owned by Form1 for the app's whole lifetime (connecting,
        // subscribing, and disposing them) — not this window.
    }

    // Fires after every options-chain poll tick (Form1.OtmOptionsUpdated) — filters by symbol
    // since one shared gateway instance can be feeding multiple ticker windows.
    private void TradeGateway_OtmOptionsUpdated(string symbol, IReadOnlyList<OtmOption> calls, IReadOnlyList<OtmOption> puts)
    {
        if (symbol != _symbol || IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            UpdateOtmButtons(_callButtons, calls, "CALL");
            UpdateOtmButtons(_putButtons, puts, "PUT");
        });
    }

    private static void UpdateOtmButtons(Button[] buttons, IReadOnlyList<OtmOption> options, string rowType)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (i >= options.Count)
            {
                btn.Visible = false;
                btn.Tag = null;
                continue;
            }

            var o = options[i];
            btn.Text = $"{o.Quote.StrikePrice:0.##}\r\n{o.Quote.Bid:0.00}/{o.Quote.Ask:0.00}";
            btn.Tag = (RowType: rowType, o.Quote.StrikePrice, o.Quote.Bid, o.Quote.Ask, o.Level);
            btn.Visible = true;
        }
    }

    private void OnOtmButtonClick(Button btn)
    {
        if (btn.Tag is not (string rowType, decimal strike, decimal bid, decimal ask, string level)) return;
        _ = _tradeGateway.ExecuteOtmMarketOrderAsync(rowType, strike, level, bid, ask);
    }
}
