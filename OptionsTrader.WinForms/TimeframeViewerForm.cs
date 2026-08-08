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
    private readonly Form1 _form1;
    private readonly List<TimeframeChartPanel> _panels = new();
    private string _symbol = string.Empty;

    // ---- SpotPrice reply + cross-watch (all in-memory, RTH-only — see PollTimer_Tick) ----
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly TimeSpan RthStart = new(9, 30, 0);
    private static readonly TimeSpan RthEnd   = new(16, 0, 0);
    private static readonly TimeSpan ReplyWaitWindow = TimeSpan.FromMinutes(5);

    private sealed class PendingZoneAlert
    {
        public long MessageId;
        public string Symbol = string.Empty;
        public string Direction = string.Empty; // "Alza" or "Baja"
        public TimeframeChartPanel SourcePanel = null!;
        public List<(decimal Strike, decimal Ask)> Strikes = new();
        public string Caption = string.Empty;
        public DateTime SentAtUtc;
        public decimal? TargetSpotPrice; // set once the reply arrives
    }

    private readonly List<PendingZoneAlert> _pendingAlerts = new();
    private readonly Dictionary<TimeframeChartPanel, decimal> _lastPriceByPanel = new();
    private long _telegramUpdateOffset;
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 5000 };

    // form1: needed to read the 5 nearest OTM strikes on a confirmed rebote — only works if this
    // viewer's loaded symbol matches form1's OWN ticker (one options chain per app instance).
    public TimeframeViewerForm(SchwabStreamerClient historyClient, ICandleFeed liveFeed, Form1 form1)
    {
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _form1         = form1;

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

        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
        FormClosed += (s, e) => _pollTimer.Stop();

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
        _pendingAlerts.Clear(); // old panels are gone — any pending SpotPrice watch is meaningless now
        _lastPriceByPanel.Clear();

        for (int i = 0; i < Timeframes.Length; i++)
        {
            var (label, minutes) = Timeframes[i];
            var panel = new TimeframeChartPanel(symbol, _historyClient, _liveFeed, minutes, label,
                enableZoneRebounds: ZoneReboundTimeframes.Contains(label))
            {
                Dock   = DockStyle.Fill,
                Margin = new Padding(6, 2, 6, 6)
            };
            panel.OnZoneReboundEvent += (sourcePanel, caption, direction, price) => OnZoneRebound(sourcePanel, symbol, caption, direction, price);
            panel.OnLiveTick += price => OnPanelLiveTick(panel, price);
            _panels.Add(panel);
            _chartsHost.Controls.Add(panel, i % 2, i / 2);
        }
    }

    private void OnZoneRebound(TimeframeChartPanel sourcePanel, string symbol, string caption, string direction, decimal price)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
        _ = SendZoneReboundTelegramPushAsync(sourcePanel, symbol, caption, direction);
    }

    // Pushes the combined 4-chart snapshot to Telegram — best-effort, same as every other Telegram
    // push in the app: a failure here must never affect chart rendering/detection. Before building
    // the message/screenshot, draws the 5 nearest OTM strikes (Calls on Alza, Puts on Baja) on the
    // panel that fired the rebote — only if this viewer's symbol matches form1's own ticker (see
    // Form1.GetNearestOtmStrikes); if not, just skips the strikes and proceeds with the push.
    private async Task SendZoneReboundTelegramPushAsync(TimeframeChartPanel sourcePanel, string symbol, string caption, string direction)
    {
        try
        {
            var strikes = _form1.GetNearestOtmStrikes(symbol, calls: direction == "Alza");
            if (strikes != null)
            {
                foreach (var (strike, ask) in strikes)
                    await sourcePanel.MarkStrikeWithAskAsync(strike, ask);
            }

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
            {
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, symbol, "TimeframeZoneRebound", DateTime.Now));

                // Start listening for a SpotPrice reply (only if we actually have strikes to
                // re-quote later — if the symbol didn't match form1's ticker, there's nothing
                // useful a second push could show, so skip the whole watch).
                if (strikes != null)
                {
                    _pendingAlerts.Add(new PendingZoneAlert
                    {
                        MessageId    = messageId.Value,
                        Symbol       = symbol,
                        Direction    = direction,
                        SourcePanel  = sourcePanel,
                        Strikes      = strikes,
                        Caption      = caption,
                        SentAtUtc    = DateTime.UtcNow
                    });
                }
            }
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

    // Runs every 5s while the window is open:
    //   1. Drops any pending alert once the clock leaves RTH (9:30-16:00 ET) — both the reply-wait
    //      and the cross-watch phases are only meaningful during the trading session.
    //   2. Drops any pending alert still waiting for a reply after 5 minutes.
    //   3. If any pending alerts are still waiting for a reply, polls Telegram once (shared call,
    //      not one per alert) and matches replies by reply_to_message.message_id.
    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_pendingAlerts.Count == 0) return;

        var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).TimeOfDay;
        if (nowEastern < RthStart || nowEastern > RthEnd)
        {
            if (_pendingAlerts.Count > 0)
            {
                LogInfo("Fuera de sesión RTH — se descartan los pendientes de SpotPrice.");
                _pendingAlerts.Clear();
            }
            return;
        }

        var expired = _pendingAlerts.Where(p => p.TargetSpotPrice == null && DateTime.UtcNow - p.SentAtUtc > ReplyWaitWindow).ToList();
        foreach (var p in expired)
        {
            LogInfo($"{p.Symbol} — sin respuesta con SpotPrice en 5 min, se descarta: {p.Caption}");
            _pendingAlerts.Remove(p);
        }

        if (!_pendingAlerts.Any(p => p.TargetSpotPrice == null)) return;

        var (botToken, _) = TelegramSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(botToken)) return;

        var (ok, _, updates) = await TelegramNotifier.GetUpdatesAsync(botToken, _telegramUpdateOffset);
        if (!ok || updates.Count == 0) return;

        _telegramUpdateOffset = updates.Max(u => u.UpdateId) + 1;

        foreach (var update in updates)
        {
            if (update.ReplyToMessageId == null) continue;
            var pending = _pendingAlerts.FirstOrDefault(p => p.MessageId == update.ReplyToMessageId && p.TargetSpotPrice == null);
            if (pending == null) continue;

            var match = System.Text.RegularExpressions.Regex.Match(update.Text, @"\d+(\.\d+)?");
            if (!match.Success || !decimal.TryParse(match.Value, System.Globalization.CultureInfo.InvariantCulture, out var spotPrice))
                continue; // reply didn't contain a usable number — stays pending, maybe they retry

            pending.TargetSpotPrice = spotPrice;
            LogInfo($"{pending.Symbol} — SpotPrice recibido: {spotPrice:F2} (esperando cruce hacia {pending.Direction})");
        }
    }

    // Fires on every live tick from any of the 4 panels — checks whether the price just crossed
    // any pending alert's TargetSpotPrice in the expected direction (Alza: was below, now at/above;
    // Baja: was above, now at/below). Needs the PREVIOUS tick to detect a genuine cross, not just
    // "is currently past it" (which would also fire on every subsequent tick).
    private void OnPanelLiveTick(TimeframeChartPanel panel, decimal price)
    {
        var previous = _lastPriceByPanel.TryGetValue(panel, out var p) ? p : (decimal?)null;
        _lastPriceByPanel[panel] = price;
        if (previous == null || _pendingAlerts.Count == 0) return;

        var crossed = _pendingAlerts
            .Where(a => a.SourcePanel == panel && a.TargetSpotPrice != null)
            .Where(a => a.Direction == "Alza"
                ? previous.Value < a.TargetSpotPrice!.Value && price >= a.TargetSpotPrice!.Value
                : previous.Value > a.TargetSpotPrice!.Value && price <= a.TargetSpotPrice!.Value)
            .ToList();

        foreach (var alert in crossed)
        {
            _pendingAlerts.Remove(alert);
            _ = SendCrossConfirmedPushAsync(alert);
        }
    }

    // Second push once the SpotPrice cross confirms — appends "   Bid=xxx" to each of the SAME 5
    // Stk lines drawn at rebote time (doesn't remove/redraw them), then sends the combined
    // snapshot again with a note that the cross confirmed.
    private async Task SendCrossConfirmedPushAsync(PendingZoneAlert alert)
    {
        try
        {
            var bids = _form1.GetBidForStrikes(alert.Symbol, calls: alert.Direction == "Alza", alert.Strikes.Select(s => s.Strike));
            if (bids != null)
            {
                foreach (var (strike, bid) in bids)
                    await alert.SourcePanel.AppendStrikeLabelAsync(strike, $"   Bid={bid.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

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
            var path = Path.Combine(folder, $"{alert.Symbol}_TimeframeZoneReboundCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var caption = $"{alert.Caption} — SpotPrice {alert.TargetSpotPrice:F2} cruzado";
            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{alert.Symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, alert.Symbol, "TimeframeZoneReboundCross", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(alert.Symbol, caption, path);
            else
                LogTelegramPushFailure(detail);

            LogInfo(caption);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    private void LogInfo(string message)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
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
