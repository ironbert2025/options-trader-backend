using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Read-only "price action from several perspectives" viewer for ONE symbol at a time — a 2x2 grid
// of TimeframeChartPanel (5m / 15m / 1h / 4h, all RTH+Overnight), fed by the same shared live
// streamer as the per-ticker Live Chart windows. No drawing tools beyond DZ/SZ, no SMA/Bollinger/
// Piso-Techo — just candles, for quickly comparing how price looks across timeframes. The one
// exception: Demand/Supply Zone rebote detection runs on the 5m and 15m charts (see
// TimeframeChartPanel's enableZoneRebounds) — a confirmed rebote pushes the combined 4-chart
// snapshot to Telegram and logs it below the charts.
public class TimeframeViewerForm : Form
{
    // Order matches grid position (row-major: top-left, top-right, bottom-left, bottom-right).
    private static readonly (string Label, int IntervalMinutes)[] Timeframes =
    {
        ("4h", 240), ("5m", 5), ("1h", 60), ("15m", 15)
    };

    // Only these 2 timeframes track Demand/Supply zones for rebote detection — 1h/4h can still
    // draw zones (DZ/SZ is armed on all 4 via the shared button) but purely visually.
    private static readonly HashSet<string> ZoneReboundTimeframes = new() { "5m", "15m" };

    private readonly ComboBox _cmbSymbol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(8, 8), Size = new Size(120, 24) };
    private readonly Button _btnCargar   = new() { Text = "Cargar", Location = new Point(136, 8), Size = new Size(70, 24) };
    private readonly Button _btnDzSz     = new() { Text = "DZ/SZ", Location = new Point(216, 8), Size = new Size(80, 26) };

    private readonly TableLayoutPanel _chartsHost = new()
    {
        Location = new Point(8, 40), Size = new Size(1296, 907), // 90% of 1440x1008
        ColumnCount = 2, RowCount = 2
    };

    // Log of "Rebote en Zona" events from the 5m/15m panels — same idea as MultiChartForm's crossLog.
    private readonly TextBox _txtEventLog = new()
    {
        Location = new Point(8, 955), Size = new Size(1296, 90),
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 8.5F), BackColor = Color.Black, ForeColor = Color.LightGreen
    };

    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly List<TimeframeChartPanel> _panels = new();
    private string _symbol = string.Empty;

    public TimeframeViewerForm(SchwabStreamerClient historyClient, ICandleFeed liveFeed)
    {
        _historyClient = historyClient;
        _liveFeed      = liveFeed;

        Text          = "Multi-Timeframe Viewer";
        Width         = 1323;
        Height        = 1100;
        StartPosition = FormStartPosition.CenterScreen;

        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        Controls.Add(_cmbSymbol);
        Controls.Add(_btnCargar);
        Controls.Add(_btnDzSz);
        Controls.Add(_chartsHost);
        Controls.Add(_txtEventLog);

        _btnCargar.Click += (s, e) => LoadSelectedSymbol();

        // Single shared toggle — arms/disarms DZ/SZ drawing on all 4 charts at once; a zone can be
        // drawn on any one of them. Only 5m/15m actually evaluate rebotes on what gets drawn.
        _btnDzSz.Click += async (s, e) =>
        {
            bool on = false;
            foreach (var panel in _panels) on = await panel.ToggleDzSzModeAsync();
            _btnDzSz.BackColor = on ? Color.LightGreen : SystemColors.Control;
        };

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
        _symbol = symbol;

        _chartsHost.Controls.Clear();
        foreach (var panel in _panels) panel.Dispose();
        _panels.Clear();
        _btnDzSz.BackColor = SystemColors.Control; // new panels always start unarmed

        for (int i = 0; i < Timeframes.Length; i++)
        {
            var (label, minutes) = Timeframes[i];
            var panel = new TimeframeChartPanel(symbol, _historyClient, _liveFeed, minutes, label,
                enableZoneRebounds: ZoneReboundTimeframes.Contains(label))
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6, 2, 6, 6)
            };
            panel.OnZoneReboundEvent += (caption, direction, price) => OnZoneRebound(symbol, caption, direction, price);
            _panels.Add(panel);
            _chartsHost.Controls.Add(panel, i % 2, i / 2);
        }
    }

    private void OnZoneRebound(string symbol, string caption, string direction, decimal price)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
        _ = SendZoneReboundTelegramPushAsync(symbol, caption);
    }

    // Pushes the combined 4-chart snapshot to Telegram — best-effort, same as every other Telegram
    // push in the app: a failure here must never affect chart rendering/detection.
    private async Task SendZoneReboundTelegramPushAsync(string symbol, string caption)
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 4 charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{symbol}_TimeframeZoneRebound_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, symbol, "TimeframeZoneRebound", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(symbol, caption, path);
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
        if (IsDisposed) return;
        BeginInvoke(() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
    }

    // Renders the 4 charts (via WebView2, not a screen capture) and stitches them in the same 2x2
    // layout shown on screen (top-left/top-right/bottom-left/bottom-right = _panels order), with a
    // yellow timeframe label centered at the top of each — same visual pattern as
    // MultiChartForm.CaptureCombinedChartImageAsync. Returns null if any panel isn't ready.
    private const int PanelGap = 6;
    private static readonly Color PanelGapColor = Color.FromArgb(58, 58, 58);
    private static readonly Color PanelLabelColor = Color.FromArgb(245, 216, 0);

    private async Task<Bitmap?> CaptureCombinedChartImageAsync()
    {
        if (_panels.Count != Timeframes.Length) return null;

        var images = new Bitmap[_panels.Count];
        try
        {
            for (int i = 0; i < _panels.Count; i++)
                images[i] = await _panels[i].CaptureImageAsync();

            var colWidth  = images.Max(img => img.Width);
            var rowHeight = images.Max(img => img.Height);
            var width  = colWidth * 2 + PanelGap;
            var height = rowHeight * 2 + PanelGap;
            var combined = new Bitmap(width, height);
            using (var g = Graphics.FromImage(combined))
            using (var labelFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(PanelLabelColor))
            {
                g.Clear(PanelGapColor);
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    var x = (i % 2) * (colWidth + PanelGap);
                    var y = (i / 2) * (rowHeight + PanelGap);
                    g.DrawImage(img, x, y);

                    var label = Timeframes[i].Label;
                    var labelSize = g.MeasureString(label, labelFont);
                    var labelX = x + (img.Width - labelSize.Width) / 2f;
                    g.DrawString(label, labelFont, labelBrush, labelX, y + 8f);
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
