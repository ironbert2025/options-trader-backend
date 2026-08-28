using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;
using System.Linq;

namespace OptionsTrader.WinForms;

// Single window (one per ticker) hosting the 3 live-chart panels (1h / 15m RTH / 15m
// RTH+Overnight) side by side horizontally.
//
// Panel 1 (1h) + panel 2 (15m RTH), their own toolbars, and every event wiring that only ever
// involves those two panels now live in TwoPanelChartsControl (see that file) — extracted into a
// real UserControl so it can ALSO be embedded directly on Form1's "Charts" tab with correct
// keyboard routing (a nested non-top-level Form, which this window used to be embedded as there,
// can't receive keys like Delete on its WebView2 controls). This form still owns panel 3 (15m
// RTH+Overnight) directly, plus everything that necessarily spans all 3 panels (Telegram pushes
// using the combined 3-chart screenshot, cross-panel mirroring of H-Lines/strike lines/ATH, the
// shared event log, the mirrored options/trades grids) — none of which panel 3-less embedded
// usages need.
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
    private readonly string _symbol;
    private readonly Form1 _form1;

    // Panel 1 (1h) + panel 2 (15m RTH), their toolbars and own event wiring — see
    // TwoPanelChartsControl. Kept as a field for CaptureCombinedChartImageAsync (trade snapshot)
    // and every public method below that reaches into panel 1/2.
    private TwoPanelChartsControl _twoPanelControl = null!;

    // Convenience aliases so the rest of this class (largely unchanged from before the panel 1/2
    // extraction) can keep referring to "_hourlyPanel"/"_rthPanel" as before.
    private ChartPanel? _hourlyPanel => _twoPanelControl.HourlyPanel;
    private ChartPanel? _rthPanel => _twoPanelControl.RthPanel;

    // Kept for CaptureCombinedChartImageAsync (trade snapshot) — same instance the constructor's
    // local variable of the same name points to, just also reachable afterward.
    private ChartPanel? _overnightPanel;

    // The mirrored options grid ("Hoy"/"Próxima" tabs) and mirrored trades grid both moved into
    // TwoPanelChartsControl (see that file) — they're not panel-1/2-specific in what they show,
    // but Form1's Charts tab (which only ever constructs a TwoPanelChartsControl, no MultiChartForm)
    // needs them too, so there's exactly one implementation instead of two. _twoPanelControl adds
    // them to ITS OWN Controls (Dock=Right / Dock=Bottom) — nothing left to declare here.

    public MultiChartForm(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, Form1 form1)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _form1         = form1;

        Text          = $"Live Charts — {symbol}";
        Width         = 1395; // +345 for the live options grid on the right
        Height        = 620; // +90 for the mirrored trades grid below the charts (2 rows tall)
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = SystemColors.Control; // visible in the gaps between/around the 3 panels

        // Panel 1 (1h) + panel 2 (15m RTH) — own toolbar, own chart layout, own event log — see
        // TwoPanelChartsControl. hourlyPanel/rthPanel aliases kept below so the rest of this
        // constructor (largely unchanged) can keep referring to them exactly as before.
        _twoPanelControl = new TwoPanelChartsControl(symbol, historyClient, liveFeed, form1) { Dock = DockStyle.Fill };
        // This window sends its own Piso/Techo Telegram push below (3-panel image) — see
        // SendPisoTechoTelegramPushAsync — so the control's own 2-panel-only push must stay silent.
        _twoPanelControl.SuppressOwnPisoTechoTelegramPush = true;
        var hourlyPanel = _twoPanelControl.HourlyPanel;
        var rthPanel = _twoPanelControl.RthPanel;

        // Outer layout: column 0 hosts panel 1+2 (TwoPanelChartsControl), column 1 hosts panel 3
        // (15m RTH+Overnight)'s own toolbar+chart — same 400:300 (of 700) ratio the original
        // 3-column layout gave panels 1+2 combined (200+200) vs panel 3 (300).
        var outerLayout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1
        };
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 400f / 7));
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 300f / 7));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outerLayout.Controls.Add(_twoPanelControl, 0, 0);

        var panel3Host = new Panel { Dock = DockStyle.Fill };
        outerLayout.Controls.Add(panel3Host, 1, 0);

        // Panel 3 (15m RTH+Overnight) — shares historyClient/liveFeed with panel 1/2, only ever
        // reads events / calls the stateless REST history method, never their connection state.
        ChartPanel? overnightPanel = new ChartPanel(symbol, _historyClient, _liveFeed, ChartPanelMode.Fifteen_Full)
        {
            Dock = DockStyle.Fill
        };
        _overnightPanel = overnightPanel;

        // Shared ATH checkbox lives on panel 1/2's own toolbar (TwoPanelChartsControl) and already
        // toggles panel 1/2 there — this extra handler also toggles panel 3, reproducing the
        // original single-handler's "toggle all 3 at once" behavior.
        _twoPanelControl.AthCheckBox.CheckedChanged += async (s, e) =>
        {
            if (overnightPanel != null) await overnightPanel.SetAllTimeHighVisibleAsync(_twoPanelControl.AthCheckBox.Checked);
        };

        // Shared H-Line button (panel 2's toolbar) — the control's own Click handler (subscribed
        // first) toggles panel 1/2 and sets an interim button color from panel 2's result; this
        // extra handler toggles panel 3 and overwrites the color with ITS result, reproducing the
        // original single-handler's "last panel toggled wins the button color" behavior (panel 3
        // was toggled last there too).
        _twoPanelControl.HLineButton.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleHLineModeAsync();
            _twoPanelControl.HLineButton.BackColor = on ? Color.LightSalmon : SystemColors.Control;
        };

        // Shared Text button (panel 2's toolbar) — same split pattern as H-Line above.
        _twoPanelControl.TextButton.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleTextModeAsync(_twoPanelControl.ChartTextTextBox.Text);
            _twoPanelControl.TextButton.BackColor = on ? Color.LightBlue : SystemColors.Control;
        };
        // When panel 1 or 2 places the text, also disarm panel 3 — mirrors the original 3-way
        // disarm-the-others-on-placement behavior.
        _twoPanelControl.OnTextPlaced += placedOn =>
        {
            if (overnightPanel != null && overnightPanel != placedOn) _ = overnightPanel.ToggleTextModeAsync(string.Empty);
        };
        // When panel 3 places the text, disarm panel 1/2 (both, since neither equals panel 3) and
        // reset the shared button — via the control's own internal disarm logic, same as it runs
        // for its own panels' placements.
        if (overnightPanel != null)
            overnightPanel.OnTextPlacedEvent += () => _twoPanelControl.DisarmTextModeExcept(overnightPanel);

        // Drawing tools — all only apply to the 15m RTH+Overnight panel, so they live in panel 3's
        // own toolbar strip, docked above its chart within panel3Host.
        var toolsHost = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(6, 4, 6, 0) };

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
        // Per-symbol "send trade data to API/S3" toggle — when off, a trade opened from this
        // window's own options grid (colStrikeLive click) still opens normally in the grid/log,
        // but SaveTradeToApiAsync skips the POST (falls back to a local negative id, same
        // mechanism as an unreachable API), which UploadScreenshotAsync and the close Telegram
        // push already treat as "keep this trade fully local" — see those in Form1.cs. Persisted
        // per ticker (tickers.json), same pattern as chkTelegramEvents below.
        var chkAws = new CheckBox
        {
            Text     = "AWS",
            Location = new Point(236, 8),
            AutoSize = true,
            Checked  = Form1.IsAwsEnabledFor(_symbol)
        };
        chkAws.CheckedChanged += (s, e) => Form1.SetAwsEnabledFor(_symbol, chkAws.Checked);

        var btnClear = new Button
        {
            Text   = "Clear",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size   = new Size(70, 24)
        };
        btnClear.Location = new Point(toolsHost.Width - btnClear.Width, 4);

        // Toggles the 15m RTH+Overnight panel between 5-minute and 15-minute candles — label
        // stays "5Min" (same toggle-button convention as DZ/SZ/T-Line/etc.), BackColor shows
        // whether 5m is currently active.
        var btn5Min = new Button
        {
            Text     = "5Min",
            Location = new Point(0, 30),
            Size     = new Size(70, 24)
        };

        // Stops the auto-push-on-every-closed-candle Telegram loop that starts automatically once
        // a Demand/Supply zone rebote confirms (see ChartPanel.OnAutoZonePushTickEvent) — this is
        // the ONLY way to stop it; a future rebote (a different zone) re-arms it again.
        var btnStopPush = new Button
        {
            Text     = "Stop Push",
            Location = new Point(152, 30),
            Size     = new Size(80, 24)
        };

        // Per-ticker "send events to Telegram" toggle — gates only the event pushes (Piso/Techo,
        // T-Line, Abriendo la Volatilidad, Demand/Supply Zone, auto-push); trade open/close pushes
        // are untouched. Persisted per symbol in tickers.json via Form1.SetTelegramEnabledFor.
        var chkTelegramEvents = new CheckBox
        {
            Text     = "Telegram",
            Location = new Point(236, 34),
            AutoSize = true,
            Checked  = Form1.IsTelegramEnabledFor(_symbol)
        };
        chkTelegramEvents.CheckedChanged += (s, e) => Form1.SetTelegramEnabledFor(_symbol, chkTelegramEvents.Checked);

        // Live "time — price" readout, updated on every raw tick this panel receives via the
        // WebSocket — not tied to candle formation, so it updates even mid-bucket.
        var lblLiveTick = new Label
        {
            Text      = string.Empty,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location  = new Point(0, 64),
            Size      = new Size(toolsHost.Width, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Consolas", 9F),
            ForeColor = Color.DeepSkyBlue
        };
        if (overnightPanel != null)
        {
            overnightPanel.OnLiveTick += (eastern, price) =>
            {
                if (lblLiveTick.IsDisposed || !lblLiveTick.IsHandleCreated) return;
                lblLiveTick.BeginInvoke(() => lblLiveTick.Text = $"{eastern:HH:mm:ss}  {price:F2}");
            };
        }

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
        // chart.html auto-disarms the rect tool itself once the 2nd click completes one — per
        // explicit request, reset the button color to match instead of staying armed-looking.
        if (overnightPanel != null) overnightPanel.OnRectPlacedEvent += () => btnRect.BackColor = SystemColors.Control;
        btnClear.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            await overnightPanel.ClearDrawingsAsync();
            btnDzSz.BackColor = SystemColors.Control;
            btnRect.BackColor = SystemColors.Control;
            _twoPanelControl.ArrowButton.BackColor = SystemColors.Control;
            _twoPanelControl.HLineButton.BackColor = SystemColors.Control;
        };
        btn5Min.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var is5Min = await overnightPanel.ToggleIntervalAsync();
            btn5Min.BackColor = is5Min ? Color.LightBlue : SystemColors.Control;
        };
        // Panel-3 half of the shared diagonal Arrow arm/disarm — TwoPanelChartsControl's own Click
        // handler (attached first) toggles panel 1/2; this extra handler on the SAME button also
        // toggles panel 3, same "sequential toggle → last result wins the button color" behavior
        // as HLineButton/TextButton above (panel 3 was toggled last there too).
        _twoPanelControl.ArrowButton.Click += async (s, e) =>
        {
            if (overnightPanel == null) return;
            var on = await overnightPanel.ToggleArrowModeAsync();
            _twoPanelControl.ArrowButton.BackColor = on ? Color.LightYellow : SystemColors.Control;
        };
        btnStopPush.Click += (s, e) => overnightPanel?.StopAutoZonePush();

        toolsHost.Controls.Add(btnDzSz);
        toolsHost.Controls.Add(btnRect);
        toolsHost.Controls.Add(chkAws);
        toolsHost.Controls.Add(btnClear);
        toolsHost.Controls.Add(btn5Min);
        toolsHost.Controls.Add(btnStopPush);
        toolsHost.Controls.Add(chkTelegramEvents);
        toolsHost.Controls.Add(lblLiveTick);

        // Chart host below the toolbar — Padding stands in for the TableLayoutPanel cell Margin
        // panel 3's chart used to get from the original 3-column `layout` panel (Dock=Fill doesn't
        // honor Margin on its own, only a container's Padding), same 6/2/6/6 gap.
        var panel3ChartHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6) };
        panel3ChartHost.Controls.Add(overnightPanel);
        panel3Host.Controls.Add(panel3ChartHost);
        panel3Host.Controls.Add(toolsHost);

        if (hourlyPanel != null)
        {
            // T-Line + SMA20 breakout — pushes the combined 3-chart image, same as a trade close.
            hourlyPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                // BeginInvoke — fires from Streamer_OnNewCandle's background (WebSocket) thread,
                // and SendTLineSignalTelegramPushAsync touches CoreWebView2 via
                // CaptureCombinedChartImageAsync (same threading bug class as AutoZonePush,
                // already fixed elsewhere in this file) — was previously called OUTSIDE the
                // BeginInvoke below, which only protected the crossLog line.
                BeginInvoke(() =>
                {
                    AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    _ = SendTLineSignalTelegramPushAsync(message);
                });
            };

            // SMA cross watch (Daily) — armed from DailyChartForm's "SMA Watch" buttons. Event
            // log already appended inside ChartPanel.EvaluateSmaCrossWatches itself (same as
            // EvaluateTLineSignal does for its own signal); this just handles the Telegram push.
            // BeginInvoke — same threading fix as hourlyPanel.OnTLineSignalEvent above.
            hourlyPanel.OnSmaCrossEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    _ = SendSmaCrossTelegramPushAsync(message);
                });
            };

            // Daily-candle bounce off SMA20 — purely informational, log only (no Telegram, no
            // automatic action; the user checks this window in the morning and acts manually).
            hourlyPanel.OnDailyBounceEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };
        }

        if (rthPanel != null)
        {
            // Panel 2's own T-Lines are independent from panel 1's — same breakout signal,
            // evaluated against panel 2's own SMA20/candles, logged/pushed identically.
            rthPanel.OnTLineSignalEvent += message =>
            {
                if (IsDisposed) return;
                // BeginInvoke — same threading fix as hourlyPanel.OnTLineSignalEvent above.
                BeginInvoke(() =>
                {
                    AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
                    _ = SendTLineSignalTelegramPushAsync(message);
                });
            };

            // "Cruce de vela con PM" — log-only, per explicit request: written directly to the
            // per-symbol events .md with the combined 3-panel screenshot, never crossLog, never
            // Telegram. BeginInvoke — fires from Streamer_OnNewCandle's background (WebSocket)
            // thread, and CaptureCombinedChartImageAsync touches CoreWebView2 (same threading bug
            // class as AutoZonePush, already fixed elsewhere in this file).
            rthPanel.OnPmCrossEvent += caption =>
            {
                if (IsDisposed) return;
                BeginInvoke(async () =>
                {
                    // The screenshot is a nice-to-have, not the event itself — a failed/timed-out
                    // capture (e.g. a minimized window, see ChartPanel.CaptureImageAsync's timeout)
                    // used to silently drop the WHOLE event here, with zero trace anywhere that a
                    // PM cross even happened. Always log the text; only skip the image if capture
                    // failed, same "never silently swallow" fix applied elsewhere this session.
                    string? path = null;
                    try
                    {
                        using var combined = await CaptureCombinedChartImageAsync();
                        if (combined != null)
                        {
                            var folder = @"C:\OptionsTraderPush";
                            Directory.CreateDirectory(folder);
                            path = Path.Combine(folder, $"{_symbol}_PMCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    catch
                    {
                        path = null; // best-effort — the event still gets logged below without an image
                    }

                    EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
                });
            };
        }

        // Demand Zone rebote (15m RTH+Overnight panel) — self-contained in ChartPanel (pushes its
        // own screenshot to Telegram + EventLogStore, same as Cross-SMA); just mirror the caption
        // into this window's log too.
        if (overnightPanel != null)
        {
            overnightPanel.OnDemandZoneReboundEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // Supply Zone rebote — symmetric counterpart, same self-contained pattern.
            overnightPanel.OnSupplyZoneReboundEvent += message =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendCrossLog($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"));
            };

            // Auto-push loop armed by either rebote above — fires on EVERY closed 15m candle from
            // then on, pushing the combined 3-chart snapshot each time, until "Stop Push" is
            // clicked (see btnStopPush) or a fresh rebote later re-arms it.
            // BeginInvoke — this event fires from Streamer_OnNewCandle's background (WebSocket)
            // thread, and SendAutoZonePushAsync eventually touches CoreWebView2 (via
            // CaptureCombinedChartImageAsync) — a direct call from that thread throws
            // "CoreWebView2 can only be accessed from the UI thread.", surfaced as a Telegram push
            // failure instead of an obvious crash (same threading bug class as the PM indicator/
            // Piso-Techo events elsewhere in this file, all of which already marshal here).
            overnightPanel.OnAutoZonePushTickEvent += candle =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => _ = SendAutoZonePushAsync(candle));
            };
        }

        // "Abriendo la Volatilidad" (arming panel 2's watch) + crossLog for the 1h panel's own
        // Piso/Techo Cruce/Rebote — wired inside TwoPanelChartsControl itself (see its constructor)
        // so it also works when the Charts tab runs standalone, with no MultiChartForm popup at
        // all; crossLog goes through the SAME textbox instance either way (AppendCrossLog delegates
        // into _twoPanelControl.AppendLog), so it must not be duplicated here.
        //
        // Telegram push is the one exception kept here rather than moved: the popup's version needs
        // the full 3-panel combined image (this window's own CaptureCombinedChartImageAsync), while
        // TwoPanelChartsControl's own push (used when the Charts tab runs standalone) only has
        // panel 1+2 to work with — per explicit request, the popup keeps working exactly as before.
        if (hourlyPanel != null)
        {
            hourlyPanel.OnPisoTechoResolvedEvent += (evento, pisoTecho, caption) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => _ = SendPisoTechoTelegramPushAsync(caption));
            };
        }

        // "Abriendo Bollinger con Volatilidad" — logs the exact moment "BB" starts showing on
        // EITHER panel (1h or 15m RTH), already persisted to EventLogStore by ChartPanel itself
        // (see OnBollingerOpeningEvent). Here: mirror into crossLog with a timestamp for real-time
        // visibility, AND save the combined 3-chart screenshot into the events .md — no Telegram
        // push for this one, just the local record (see SaveBollingerOpeningSnapshotAsync).
        if (hourlyPanel != null)
        {
            hourlyPanel.OnBollingerOpeningEvent += caption =>
            {
                if (IsDisposed) return;
                // BeginInvoke — same threading fix as OnTLineSignalEvent above (SaveBollingerOpeningSnapshotAsync also touches CoreWebView2).
                BeginInvoke(() =>
                {
                    AppendCrossLog($"{DateTime.Now:HH:mm:ss}  [1h] {caption}{Environment.NewLine}");
                    _ = SaveBollingerOpeningSnapshotAsync(caption);
                });
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnBollingerOpeningEvent += caption =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    AppendCrossLog($"{DateTime.Now:HH:mm:ss}  [15m RTH] {caption}{Environment.NewLine}");
                    _ = SaveBollingerOpeningSnapshotAsync(caption);
                });
            };
        }

        // Piso/Techo reference line + Daily PM: mirrors onto panel 3 (RTH+Overnight) only —
        // TwoPanelChartsControl's own constructor already mirrors the same events onto panel 2
        // (rthPanel), so this window only ever needs to add panel 3's edge on top, same split
        // pattern as the Stk-line/H-Line/ATH mirroring mesh below.
        if (hourlyPanel != null && overnightPanel != null)
        {
            hourlyPanel.OnPisoTechoLevelReadyEvent += (period, price) =>
            {
                // BeginInvoke — this event can fire from Streamer_OnNewCandle's background
                // (WebSocket) thread, and a direct ExecuteScriptAsync call from that thread
                // silently fails (same threading bug the PM indicator had).
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    var sessionEnd   = GetTodaySessionEndFakeEpoch();
                    _ = overnightPanel.MarkPisoTechoRefLineAsync(period, price, sessionStart, sessionEnd);
                });
            };

            // Daily "PM" (SMA20) solid yellow reference line — computed on the 1h panel only
            // (EvaluateDailyPmAndBb), relayed to panel 3 here (panel 1/2 already handled inside
            // TwoPanelChartsControl), same anchor (yesterday's last close, extending through today)
            // the red dashed prev-day-close line uses, per explicit request ("igual que la línea
            // roja... hasta el final").
            hourlyPanel.OnDailyPmValueEvent += price =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    var sessionStart = GetTodaySessionStartFakeEpoch();
                    _ = overnightPanel.MarkDailyPmLineAsync(price, sessionStart);
                });
            };

            hourlyPanel.OnPisoTechoLevelRemovedEvent += period =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => _ = overnightPanel.RemovePisoTechoRefLineAsync(period));
            };

            // Race fix: hourlyPanel's HandleCreated (added to the layout earlier in this
            // constructor) can fire EvaluatePisoTechoOnce — and this very event — before the
            // subscription above ever runs, especially when its history loads fast (e.g. plenty of
            // HourlyCandleStore data already cached locally). Catch up immediately in that case.
            // TwoPanelChartsControl's own constructor already did this once for panel 1/2's sake;
            // calling it again here is harmless (idempotent re-draws) and ensures panel 3 catches
            // up too, even though its subscription above runs strictly after that first replay.
            hourlyPanel.ReplayPisoTechoLevels();
        }

        // Telegram push failures previously vanished silently (fire-and-forget from every call
        // site) — mirror the failure detail into crossLog on whichever panel it happened on, so a
        // missed push is diagnosable instead of just "the event logged but nothing arrived".
        foreach (var panel in new[] { hourlyPanel, rthPanel, overnightPanel })
        {
            if (panel == null) continue;
            panel.OnTelegramPushFailedEvent += detail =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => AppendCrossLog($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
            };
            panel.OnPrevDayHiLoDebugEvent += detail =>
            {
                // Diagnostic-only — written to disk instead of crossLog so it doesn't clutter the
                // visible UI now that the underlying "H-Lines only on one panel" bug is confirmed
                // fixed; kept on disk for later review instead of being thrown away outright.
                try
                {
                    Directory.CreateDirectory(@"C:\OptionsData\EventLog");
                    File.AppendAllText(@"C:\OptionsData\EventLog\prevday_hilo_debug.log",
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {detail}{Environment.NewLine}");
                }
                catch { /* best-effort diagnostic logging */ }
            };
        }

        // Stk line delete / H-Line delete / H-Line draw / ATH: mirrored across all 3 panels when
        // this control hosts panel 3 too. The panel1<->panel2 edge of each mesh is already wired
        // INSIDE TwoPanelChartsControl itself (so it also works standalone on Form1's "Charts" tab,
        // which has no panel 3 at all) — only panel 3's own edges get added here, never
        // re-wiring panel1<->panel2 too, or a draw/delete would double-fire across that pair.
        if (overnightPanel != null)
        {
            overnightPanel.OnStrikeDeletedEvent += price =>
            {
                if (hourlyPanel != null) _ = hourlyPanel.RemoveStrikeLineAsync(price);
                if (rthPanel != null) _ = rthPanel.RemoveStrikeLineAsync(price);
            };
            overnightPanel.OnHLineDeletedEvent += price =>
            {
                if (hourlyPanel != null) _ = hourlyPanel.RemoveHLineAsync(price);
                if (rthPanel != null) _ = rthPanel.RemoveHLineAsync(price);
            };
            overnightPanel.OnHLineDrawnEvent += (time, price) =>
            {
                if (hourlyPanel != null) _ = hourlyPanel.AddMirroredHLineAsync(time, price);
                if (rthPanel != null) _ = rthPanel.AddMirroredHLineAsync(time, price);
            };
        }
        if (hourlyPanel != null)
        {
            hourlyPanel.OnStrikeDeletedEvent += price => { if (overnightPanel != null) _ = overnightPanel.RemoveStrikeLineAsync(price); };
            hourlyPanel.OnHLineDeletedEvent += price => { if (overnightPanel != null) _ = overnightPanel.RemoveHLineAsync(price); };
            hourlyPanel.OnHLineDrawnEvent += (time, price) => { if (overnightPanel != null) _ = overnightPanel.AddMirroredHLineAsync(time, price); };
            // All-Time High: the 1h panel is the only one that persists a new value (at the RTH
            // close, see ChartPanel.EvaluateAllTimeHighAtClose) — mirror it onto the other panels'
            // reference lines so all stay in sync for the rest of the day (and on the next chart
            // open, each panel loads it fresh from AllTimeHighStore on its own anyway).
            hourlyPanel.OnAllTimeHighUpdatedEvent += newValue =>
            {
                if (overnightPanel != null) _ = overnightPanel.MarkAllTimeHighAsync(newValue);
            };
        }
        if (rthPanel != null)
        {
            rthPanel.OnStrikeDeletedEvent += price => { if (overnightPanel != null) _ = overnightPanel.RemoveStrikeLineAsync(price); };
            rthPanel.OnHLineDeletedEvent += price => { if (overnightPanel != null) _ = overnightPanel.RemoveHLineAsync(price); };
            rthPanel.OnHLineDrawnEvent += (time, price) => { if (overnightPanel != null) _ = overnightPanel.AddMirroredHLineAsync(time, price); };
        }

        // Options grid (Dock=Right) + trades grid (Dock=Bottom) are added to _twoPanelControl's
        // OWN Controls collection (built alongside panel 1/2 there) — nothing to add here.
        Controls.Add(outerLayout);

        // historyClient/liveFeed are owned by Form1 for the app's whole lifetime (connecting,
        // subscribing, and disposing them) — not this window.
    }

    // T-Lines drawn on a DailyChartForm's "Hora"/"15 Min" tabs replicate onto this window's
    // corresponding live panel (1h/RTH) and persist there too — per explicit request, one-way
    // only. Public (not just wired for THIS window's own "Daily" button) so Form1's own "Daily"
    // button — which opens a DailyChartForm with no MultiChartForm involved at all — can wire the
    // same mirroring onto an already-open (or later-opened) live chart for the same symbol; see
    // Form1.BtnDaily_Click/BtnLiveChart_Click. Just delegates to the panel 1/2 control, which owns
    // the actual mirroring logic (and the SMA-watch buttons it also keeps in sync).
    public void AttachDailyMirroring(DailyChartForm dailyForm) => _twoPanelControl.AttachDailyMirroring(dailyForm);

    // Feeds a fresh spot price (from Form1's ~6s options-chain polling, not the streaming feed)
    // into all 3 panels' currently-forming candle — used while LEVEL_ONE_EQUITIES is disabled, so
    // the live chart still tracks something closer to real-time than waiting a full minute for
    // the next CHART_EQUITY bar. Panel 1/2's half is delegated to the control; panel 3 is fed
    // directly here.
    public void FeedPollingPrice(decimal price, DateTime utcTime)
    {
        _twoPanelControl.FeedPollingPrice(price, utcTime);
        _overnightPanel?.FeedPollingPrice(price, utcTime);
    }

    // Red "Expired!!!" marker on the 15m RTH panel (middle chart) only — fired when a trade
    // auto-closes at 4pm ET because it expires today.
    public Task MarkExpiredOnRthChartAsync() => _rthPanel?.MarkExpiredAsync() ?? Task.CompletedTask;

    // "ΔS=value" label at trade close — panels 2 (15m RTH) and 3 (15m RTH+Overnight). Originally
    // panel 3 only; panel 2 added per explicit request.
    public async Task MarkDeltaSOnOvernightChartAsync(decimal entrySpot, decimal closeSpot, decimal strike)
    {
        if (_rthPanel != null) await _rthPanel.MarkDeltaSAsync(entrySpot, closeSpot, strike);
        if (_overnightPanel != null) await _overnightPanel.MarkDeltaSAsync(entrySpot, closeSpot, strike);
    }

    // Green "Stk=xxx" line — panels 2 (15m RTH) and 3 (15m RTH+Overnight). Fired when a trade
    // (demo or real) opens. Originally panel 3 only; panel 2 added per explicit request.
    public async Task MarkStrikeOnOvernightChartAsync(decimal strike)
    {
        if (_rthPanel != null) await _rthPanel.MarkStrikeAsync(strike);
        if (_overnightPanel != null) await _overnightPanel.MarkStrikeAsync(strike);
    }

    // White spot-price line — panels 2 (15m RTH) and 3 (15m RTH+Overnight), same marker the
    // Simulator already draws on trade open/close. Fired at both. Originally panel 3 only; panel 2
    // added per explicit request.
    public async Task MarkEntrySpotOnOvernightChartAsync(decimal price)
    {
        if (_rthPanel != null) await _rthPanel.MarkEntrySpotAsync(price);
        if (_overnightPanel != null) await _overnightPanel.MarkEntrySpotAsync(price);
    }

    // Today's 9:30 AM ET, in the same "ET wall-clock digits disguised as UTC" fake-epoch units the
    // chart itself uses (ChartPanel.FakeUtcEpochSeconds) — the Piso/Techo reference line's anchor,
    // computed independently of any candle data so it can't race against history loading (see
    // markPisoTechoRefLine in chart.html for the full rationale).
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // Single choke point for panel-3/combined-screenshot crossLog writes — delegates into the
    // panel 1/2 control's own AppendLog (same premarket 9:30 AM ET filter, same textbox instance),
    // so this window still shows one unified log across all 3 panels.
    private void AppendCrossLog(string text) => _twoPanelControl.AppendLog(text);

    // Yesterday's last hourly candle (its close) — the Piso/Techo reference line's real anchor,
    // per explicit request ("así estaba implementado"): it should be born at yesterday's close and
    // run through the whole of today's RTH session, not at a fixed hour of today. Read from
    // HourlyCandleStore (already persisted/updated by the 1h panel) instead of the 1h panel's own
    // in-memory candles, so this can't race against that panel's history load. Falls back to
    // today 4:00 AM ET if no prior-day history is on disk yet (shouldn't normally happen).
    private long GetTodaySessionStartFakeEpoch()
    {
        var hourly = HourlyCandleStore.Load(_symbol);
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).Date;
        var prior = hourly
            .Select(c => (Candle: c, Date: TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone).Date))
            .Where(x => x.Date < todayEastern)
            .ToList();

        if (prior.Count > 0)
        {
            var prevDate = prior.Max(x => x.Date);
            var lastBar = prior.Where(x => x.Date == prevDate).OrderBy(x => x.Candle.Time).Last().Candle;
            return ChartPanel.FakeUtcEpochSeconds(lastBar.Time);
        }

        var fallbackEastern = todayEastern.AddHours(4);
        var fallbackUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fallbackEastern, DateTimeKind.Unspecified), EasternZone);
        return ChartPanel.FakeUtcEpochSeconds(fallbackUtc);
    }

    // RTH session close (16:00 ET, today) — so the Piso/Techo reference line stops there instead
    // of running off to the chart's own right edge.
    private static long GetTodaySessionEndFakeEpoch()
    {
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone).Date;
        var sessionEndEastern = todayEastern.AddHours(16);
        var sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionEndEastern, DateTimeKind.Unspecified), EasternZone);
        return ChartPanel.FakeUtcEpochSeconds(sessionEndUtc);
    }

    // Pushes the combined 3-chart snapshot to Telegram for the T-Line+SMA20 breakout signal —
    // best-effort, same as every other Telegram push in this app: a failure here must never
    // affect the chart/detection logic itself.
    private async Task SendTLineSignalTelegramPushAsync(string caption)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_TLineSignal_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "TLineSignal", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            // Best-effort — never let a Telegram failure affect the chart/detection logic, but no
            // longer silent: mirrored into crossLog same as every other push failure.
            LogTelegramPushFailure(ex.Message);
        }
    }

    // Pushes the combined 3-chart snapshot to Telegram for a Daily SMA cross watch — same pattern
    // as SendTLineSignalTelegramPushAsync above.
    private async Task SendSmaCrossTelegramPushAsync(string caption)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_SmaCross_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "SmaCross", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    // Pushes the combined 3-chart snapshot for a Piso/Techo Cruce/Rebote resolution — additional to
    // ChartPanel's own single-panel push (SendChartToTelegramAsync, fired from the 1h panel itself
    // for the SAME event), per explicit request. Same best-effort pattern as every other push here.
    // Saves the combined 3-chart screenshot for "Abriendo Bollinger con Volatilidad" into the
    // events .md — no Telegram push for this one (per explicit request, it's local-only), just
    // the same capture-and-embed step every other combined-snapshot event already does.
    private async Task SaveBollingerOpeningSnapshotAsync(string caption)
    {
        try
        {
            using var combined = await CaptureCombinedChartImageAsync();
            if (combined == null) return;

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_BollingerOpening_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
        }
        catch
        {
            // Best-effort, same as every other snapshot/push in this file — never let this affect
            // the analysis that just fired.
        }
    }

    // Pushes the combined 3-chart snapshot for a Piso/Techo Cruce/Rebote resolution — additional to
    // ChartPanel's own single-panel push (SendChartToTelegramAsync, fired from the 1h panel itself
    // for the SAME event), per explicit request. Kept here (not moved into TwoPanelChartsControl's
    // own copy) so the popup keeps pushing the full 3-panel image exactly as before — see the
    // comment on the OnPisoTechoResolvedEvent subscription above.
    private async Task SendPisoTechoTelegramPushAsync(string caption)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
                return;
            }

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_PisoTecho_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "PisoTechoCross", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                LogTelegramPushFailure(detail);
        }
        catch (Exception ex)
        {
            LogTelegramPushFailure(ex.Message);
        }
    }

    // Pushes the combined 3-chart snapshot on every closed 15m candle while the auto-push loop is
    // armed (see ChartPanel.OnAutoZonePushTickEvent) — a Demand/Supply zone rebote confirmed and
    // "Stop Push" hasn't been clicked since. Same best-effort pattern as every other Telegram push.
    private async Task SendAutoZonePushAsync(CandleData candle)
    {
        if (!Form1.IsTelegramEnabledFor(_symbol)) return;
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
                LogTelegramPushFailure("No se pudo capturar el snapshot combinado de los 3 charts.");
                return;
            }

            var caption = $"Auto-push Rebote — Close {candle.Close:F2}";

            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_AutoZonePush_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            combined.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "AutoZonePush", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
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
        BeginInvoke(() => AppendCrossLog($"{DateTime.Now:HH:mm:ss}  [Telegram] Push FAILED — {detail}{Environment.NewLine}"));
    }

    // Renders the 3 charts (via WebView2, not a screen capture) and stitches them side by side in
    // the same left-to-right order they're shown on screen (1h, 15m RTH, 15m RTH+Overnight), for
    // Form1 to save as a single trade snapshot. Returns null if any panel isn't ready.
    // Gray gap between panels (so each chart is visually distinct in the combined snapshot) and a
    // yellow timeframe label centered at the top of each one — order matches the panels array.
    private const int PanelGap = 6;
    private static readonly Color PanelGapColor = Color.FromArgb(58, 58, 58);
    private static readonly Color PanelLabelColor = Color.FromArgb(245, 216, 0);
    private static readonly string[] PanelLabels = { "1 Hour", "15Min RTH", "15Min RTH+OVN" };

    public async Task<Bitmap?> CaptureCombinedChartImageAsync()
    {
        if (_hourlyPanel == null || _rthPanel == null || _overnightPanel == null) return null;

        var panels = new[] { _hourlyPanel, _rthPanel, _overnightPanel };
        var images = new Bitmap?[panels.Length];
        try
        {
            for (int i = 0; i < panels.Length; i++)
                images[i] = await panels[i].CaptureImageAsync();

            // A panel's own capture can time out (see ChartPanel.CaptureImageAsync — a minimized/
            // non-composited window) instead of throwing — bail out the same way the "any panel
            // isn't ready" case at the top of this method already does, rather than crashing on a
            // null Bitmap a few lines down.
            if (images.Any(img => img == null)) return null;

            var width  = images.Sum(img => img!.Width) + PanelGap * (images.Length - 1);
            var height = images.Max(img => img!.Height);
            var combined = new Bitmap(width, height);
            using (var g = Graphics.FromImage(combined))
            using (var labelFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(PanelLabelColor))
            {
                g.Clear(PanelGapColor);
                var x = 0;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i]!;
                    g.DrawImage(img, x, 0);

                    var labelSize = g.MeasureString(PanelLabels[i], labelFont);
                    var labelX = x + (img.Width - labelSize.Width) / 2f;
                    g.DrawString(PanelLabels[i], labelFont, labelBrush, labelX, 8f);

                    x += img.Width + PanelGap;
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
