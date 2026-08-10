using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Read-only "price action from several perspectives" viewer for ONE symbol at a time — a 2x2 grid
// of TimeframeChartPanel (5m / 15m / 1h / 4h, all RTH+Overnight), fed by the same shared live
// streamer as the per-ticker Live Chart windows. No drawing tools beyond DZ/SZ, no SMA/Bollinger/
// Piso-Techo — just candles, for quickly comparing how price looks across timeframes. The one
// exception: Demand/Supply Zone rebote detection runs on the 5m and 15m charts (see
// TimeframeChartPanel's enableZoneRebounds) — a confirmed rebote pushes a snapshot of just THAT
// chart (the one the zone is drawn on) to Telegram and logs it below the charts.
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
        public decimal? TargetSpotPrice; // set once a reply with exactly 1 number arrives

        // Set once a reply with 2 numbers arrives ("strike, spotDeCierre") instead of 1 — opens an
        // automatic demo trade in Form1's own Trades grid at TradeStrike, closed either by Form1's
        // own 300%-target auto-close (see Form1.OpenAutomaticDemoTrade) or by TargetSpotPrice
        // crossing, whichever happens first (see PollTimer_Tick/OnPanelLiveTick). When this is set,
        // TargetSpotPrice above is repurposed as the trade's close-spot instead of the plain
        // "append Bid to the 5 lines" behavior.
        public decimal? TradeStrike;
        public DataGridViewRow? TradeRow;
    }

    private readonly List<PendingZoneAlert> _pendingAlerts = new();
    private readonly Dictionary<TimeframeChartPanel, decimal> _lastPriceByPanel = new();
    private long _telegramUpdateOffset;
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 5000 };

    // form1: needed to read the 8 nearest OTM strikes on a confirmed rebote — only works if this
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
        Controls.Add(_chartsHost);
        Controls.Add(_txtEventLog);

        _btnCargar.Click += (s, e) => LoadSelectedSymbol();

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
            // Defaults to form1's OWN ticker (so the strikes/trade-demo features work out of the
            // box without the user having to manually match the dropdown) — falls back to the
            // first symbol in the list if form1 has none selected or it isn't in this list.
            var ownSymbolIndex = _form1.SelectedTickerSymbol is { } ownSymbol ? symbols.IndexOf(ownSymbol) : -1;
            _cmbSymbol.SelectedIndex = ownSymbolIndex >= 0 ? ownSymbolIndex : 0;
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
        Text = $"{symbol} — Multi-Timeframe Viewer";

        _chartsHost.Controls.Clear();
        foreach (var panel in _panels) panel.Dispose();
        _panels.Clear();
        _pendingAlerts.Clear(); // old panels are gone — any pending SpotPrice watch is meaningless now
        _lastPriceByPanel.Clear();

        var panelsByLabel = new Dictionary<string, TimeframeChartPanel>();
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
            panelsByLabel[label] = panel;

            if (label == "5m")
                _chartsHost.Controls.Add(BuildFiveMinuteCell(panel), i % 2, i / 2);
            else
                _chartsHost.Controls.Add(panel, i % 2, i / 2);
        }

        // Wired here (after all 4 panels exist) since the toolbar needs the 15m panel too, which
        // isn't built yet at the point the 5m cell itself is constructed above.
        WireFiveMinuteToolbar(panelsByLabel["5m"], panelsByLabel["15m"]);
    }

    // All the drawing-tool controls live above the 5m chart specifically, per explicit request —
    // DZ/SZ still arms all 4 panels (unchanged scope), but Arrow/Rect only apply to 5m/15m (see
    // ZoneReboundTimeframes — same 2 timeframes that evaluate rebotes).
    private readonly Button _btnDzSz  = new() { Text = "DZ/SZ", Location = new Point(0, 2),   Size = new Size(64, 24) };
    private readonly Button _btnArrow = new() { Text = "Arrow", Location = new Point(70, 2),  Size = new Size(64, 24) };
    private readonly Button _btnRect  = new() { Text = "Rect",  Location = new Point(140, 2), Size = new Size(64, 24) };

    private Panel BuildFiveMinuteCell(TimeframeChartPanel fiveMinPanel)
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 30 };
        toolbar.Controls.Add(_btnDzSz);
        toolbar.Controls.Add(_btnArrow);
        toolbar.Controls.Add(_btnRect);

        var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6, 2, 6, 6) };
        fiveMinPanel.Margin = new Padding(0);
        cell.Controls.Add(fiveMinPanel);
        cell.Controls.Add(toolbar);
        return cell;
    }

    // Re-wires the toolbar buttons' click handlers against the CURRENT day's 5m/15m panel
    // instances — called after every symbol switch, since the panels themselves get torn down and
    // rebuilt each time (see LoadSelectedSymbol).
    private void WireFiveMinuteToolbar(TimeframeChartPanel fiveMin, TimeframeChartPanel fifteenMin)
    {
        _btnDzSz.BackColor = _btnArrow.BackColor = _btnRect.BackColor = SystemColors.Control;

        _btnDzSz.Click -= BtnDzSz_Click;
        _btnArrow.Click -= BtnArrow_Click;
        _btnRect.Click -= BtnRect_Click;

        // Single shared toggle — arms/disarms DZ/SZ drawing on all 4 charts at once; a zone can be
        // drawn on any one of them. Only 5m/15m actually evaluate rebotes on what gets drawn.
        _btnDzSz.Click += BtnDzSz_Click;
        _btnArrow.Click += BtnArrow_Click;
        _btnRect.Click += BtnRect_Click;
        return;

        async void BtnDzSz_Click(object? s, EventArgs e)
        {
            bool on = false;
            foreach (var panel in _panels) on = await panel.ToggleDzSzModeAsync();
            _btnDzSz.BackColor = on ? Color.LightGreen : SystemColors.Control;
        }

        // Diagonal line + arrowhead between 2 clicks — red if 1st click is above the 2nd, green
        // otherwise (not the fixed vertical up/down arrow).
        async void BtnArrow_Click(object? s, EventArgs e)
        {
            var on1 = await fiveMin.ToggleArrowModeAsync();
            var on2 = await fifteenMin.ToggleArrowModeAsync();
            _btnArrow.BackColor = on1 || on2 ? Color.LightYellow : SystemColors.Control;
        }

        async void BtnRect_Click(object? s, EventArgs e)
        {
            var on1 = await fiveMin.ToggleRectModeAsync();
            var on2 = await fifteenMin.ToggleRectModeAsync();
            _btnRect.BackColor = on1 || on2 ? Color.LightSkyBlue : SystemColors.Control;
        }
    }

    private void OnZoneRebound(TimeframeChartPanel sourcePanel, string symbol, string caption, string direction, decimal price)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  {caption}{Environment.NewLine}"));
        _ = SendZoneReboundTelegramPushAsync(sourcePanel, symbol, caption, direction);
    }

    // Pushes a snapshot of just the panel that fired the rebote (not the other 3 charts) to
    // Telegram — best-effort, same as every other Telegram push in the app: a failure here must
    // never affect chart rendering/detection. Before building the message/screenshot, draws the
    // 8 nearest OTM strikes (Calls on Alza, Puts on Baja) on the
    // panel that fired the rebote — only if this viewer's symbol matches form1's own ticker (see
    // Form1.GetNearestOtmStrikes); if not, just skips the strikes and proceeds with the push.
    private async Task SendZoneReboundTelegramPushAsync(TimeframeChartPanel sourcePanel, string symbol, string caption, string direction)
    {
        try
        {
            var strikes = _form1.GetNearestOtmStrikes(symbol, calls: direction == "Alza", count: 8);
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

            using var combined = await sourcePanel.CaptureImageAsync();

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

        // Any open demo trade may have already hit its 300% target via Form1's OWN polling
        // (UpdateTradesPnL auto-closes it there, independent of us) — check every tick so the
        // "whichever happens first" push goes out promptly instead of waiting for the next
        // SpotPrice-cross tick that might never come if the target already closed it.
        var targetClosed = _pendingAlerts.Where(p => p.TradeRow != null && Form1.IsTradeRowClosed(p.TradeRow)).ToList();
        foreach (var p in targetClosed)
        {
            _pendingAlerts.Remove(p);
            _ = SendTradeClosedPushAsync(p, "Target 300%");
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

            // Reply can be ONE number (plain SpotPrice-only flow, existing behavior) or TWO
            // numbers separated by comma/space ("strike, spotDeCierre" — opens a demo trade).
            var numbers = System.Text.RegularExpressions.Regex.Matches(update.Text, @"\d+(\.\d+)?")
                .Select(m => decimal.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (numbers.Count == 0) continue; // reply didn't contain a usable number — stays pending, maybe they retry

            if (numbers.Count >= 2)
            {
                pending.TradeStrike = numbers[0];
                pending.TargetSpotPrice = numbers[1]; // repurposed as the trade's close-spot
                _ = OpenDemoTradeAsync(pending);
            }
            else
            {
                pending.TargetSpotPrice = numbers[0];
                LogInfo($"{pending.Symbol} — SpotPrice recibido: {numbers[0]:F2} (esperando cruce hacia {pending.Direction})");
            }
        }
    }

    // Opens the automatic demo trade in Form1's OWN Trades grid at pending.TradeStrike (CALL on
    // Alza, PUT on Baja) — only works if this viewer's symbol still matches form1's ticker. Sends
    // a text-only Telegram push confirming the open (no image, per explicit request).
    private async Task OpenDemoTradeAsync(PendingZoneAlert pending)
    {
        var row = await _form1.OpenAutomaticDemoTrade(pending.Symbol, calls: pending.Direction == "Alza", pending.TradeStrike!.Value);
        if (row == null)
        {
            LogInfo($"{pending.Symbol} — no se pudo abrir el trade demo en Strike={pending.TradeStrike:F2} (símbolo no coincide con el ticker de Form1, o sin cotización).");
            _pendingAlerts.Remove(pending);
            return;
        }

        pending.TradeRow = row;
        var entryAsk = row.Cells["colTradeEntryPrice"].Value?.ToString() ?? "?";
        var type = pending.Direction == "Alza" ? "CALL" : "PUT";
        var openCaption = $"Trade demo abierto: {type} Strike={pending.TradeStrike:F2} Entry(Ask)={entryAsk} — cierra a 300% o al cruzar {pending.TargetSpotPrice:F2}";
        LogInfo($"{pending.Symbol} — {openCaption}");
        decimal.TryParse(entryAsk, out var entryAskPrice);
        EventLogStore.Append(pending.Symbol, pending.SourcePanel.TimeframeLabel, "DemoTradeOpened", pending.Direction,
            openCaption, entryAskPrice, $"Strike={pending.TradeStrike:F2};CloseSpot={pending.TargetSpotPrice:F2}");

        var (botToken, chatId) = TelegramSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;
        var text = $"{pending.Symbol} — Trade demo abierto: {type} Strike={pending.TradeStrike:F2} Entry(Ask)={entryAsk}";
        var (ok, detail, _) = await TelegramNotifier.SendAsync(botToken, chatId, text, pending.Symbol);
        if (!ok) LogTelegramPushFailure(detail);
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
            if (alert.TradeRow != null)
                _ = CloseDemoTradeBySpotAsync(alert);
            else
                _ = SendCrossConfirmedPushAsync(alert);
        }
    }

    // Closes the demo trade by SpotPrice cross — only if the 300% target hasn't already closed it
    // (Form1's own polling can beat us to it between poll ticks; see PollTimer_Tick's target check).
    private async Task CloseDemoTradeBySpotAsync(PendingZoneAlert alert)
    {
        if (alert.TradeRow == null) return;
        if (Form1.IsTradeRowClosed(alert.TradeRow))
        {
            LogInfo($"{alert.Symbol} — el trade demo ya había cerrado por Target 300% antes del cruce de SpotPrice.");
            return;
        }

        await _form1.CloseAutomaticDemoTradeAsync(alert.TradeRow);
        await SendTradeClosedPushAsync(alert, "Cruce de SpotPrice");
    }

    // Pushes a snapshot of just the panel that opened the trade + the PnL result once a demo trade
    // closes (either reason). Shared by the spot-cross close above and the 300%-target close
    // detected in PollTimer_Tick.
    private async Task SendTradeClosedPushAsync(PendingZoneAlert alert, string closeReason)
    {
        try
        {
            var row = alert.TradeRow!;
            var entry = row.Cells["colTradeEntryPrice"].Value?.ToString() ?? "?";
            var exit  = row.Cells["colTradeCBid"].Value?.ToString() ?? "?";
            var pnl   = row.Cells["colTradePnL"].Value?.ToString() ?? "?";
            var pnlPct = row.Cells["colTradePnLPercent"].Value?.ToString() ?? "?";
            var type = alert.Direction == "Alza" ? "CALL" : "PUT";
            var caption = $"Trade demo CERRADO ({closeReason}) — {type} Strike={alert.TradeStrike:F2} Entry={entry} Exit={exit} PnL={pnl} ({pnlPct}%)";
            decimal.TryParse(exit, out var exitPrice);
            EventLogStore.Append(alert.Symbol, alert.SourcePanel.TimeframeLabel, "DemoTradeClosed", alert.Direction,
                caption, exitPrice, $"Reason={closeReason};Strike={alert.TradeStrike:F2};Entry={entry};PnL={pnl}");

            var (botToken, chatId) = TelegramSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            {
                LogTelegramPushFailure("Bot Token o Chat ID vacío");
                return;
            }

            using var combined = await alert.SourcePanel.CaptureImageAsync();

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{alert.Symbol}_TimeframeDemoTradeClosed_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{alert.Symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, alert.Symbol, "TimeframeDemoTradeClosed", DateTime.Now));
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

            using var combined = await alert.SourcePanel.CaptureImageAsync();

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{alert.Symbol}_TimeframeZoneReboundCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var caption = $"{alert.Caption} — SpotPrice {alert.TargetSpotPrice:F2} cruzado";
            EventLogStore.Append(alert.Symbol, alert.SourcePanel.TimeframeLabel, "ZoneReboundCross", alert.Direction,
                caption, alert.TargetSpotPrice ?? 0m, alert.Caption);

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

}
