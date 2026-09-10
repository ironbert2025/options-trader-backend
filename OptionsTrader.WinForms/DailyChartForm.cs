using System.Linq;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Standalone window showing up to the last 250 Daily candles for a symbol (enough for SMA100/200
// to actually have data, not just SMA20/40), with SMA20/40/100/200 and Bollinger Bands(20,2) — so
// the "PM" (Punto Medio / SMA20 slope) indicator on the 1h panel can be related back to the
// actual daily candles. A brand-new WebView2/page load, deliberately NOT a toggle on the live 1h
// panel's own chart: toggling Daily in-place there hit an unresolved rendering bug (correct data,
// correct axis range, but candles stayed invisible until a manual scroll/zoom) that survived every
// attempted fix (repaint tricks, a dedicated second series, even a real OS-level mouse scroll). A
// fresh page load doesn't carry over whatever state that bug depended on — same chart.html, same
// candlestick rendering the live panels already use without issue, just fed daily-bucketed
// candles instead of hourly ones, with no live streaming/toggle involved at all.
public class DailyChartForm : Form
{
    private Dictionary<int, Button> _smaWatchButtons = new();
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _hourlyWebView = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _fifteenWebView = new() { Dock = DockStyle.Fill };
    private readonly string _symbol;
    private readonly List<CandleData> _dailyCandles;
    private readonly SchwabStreamerClient _historyClient;

    // The currently-forming bar for each tab, kept updated live by UpdateLivePrice (per explicit
    // request — this popup used to only ever show fully-CLOSED days/hours/15-min bars, with the
    // in-progress one frozen at whatever it looked like when the window opened, so you couldn't
    // see it visually approaching a Piso/Techo level in real time). Null until InitAsync loads the
    // initial data for that tab.
    private CandleData? _lastDailyCandle;
    private CandleData? _lastHourlyCandle;
    private CandleData? _lastFifteenCandle;

    public DailyChartForm(string symbol, List<CandleData> dailyCandles, SchwabStreamerClient historyClient)
    {
        _symbol = symbol;
        _dailyCandles = dailyCandles;
        _historyClient = historyClient;

        Text          = $"{symbol} — Daily";
        Width         = 900;
        Height        = 600;
        StartPosition = FormStartPosition.CenterScreen;

        // Owner-drawn so the selected tab's header gets bolded/highlighted — same pattern
        // MultiChartForm's "Hoy"/"Próxima" tabs use, per explicit request (3 tabs here, easy to
        // lose track of which one is active since Daily/Hora/15 Min all share one chart layout).
        var tabControl = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed };
        var tabDaily = new TabPage("Daily");
        var tabHora = new TabPage("Hora");
        var tab15Min = new TabPage("15 Min");
        tabDaily.Controls.Add(_webView);
        tabHora.Controls.Add(_hourlyWebView);
        tab15Min.Controls.Add(_fifteenWebView);
        tabControl.TabPages.Add(tabDaily);
        tabControl.TabPages.Add(tabHora);
        tabControl.TabPages.Add(tab15Min);
        tabControl.DrawItem += (s, e) =>
        {
            var page = tabControl.TabPages[e.Index];
            var selected = e.Index == tabControl.SelectedIndex;
            using var backBrush = new SolidBrush(selected ? Color.FromArgb(230, 244, 255) : tabControl.BackColor);
            e.Graphics.FillRectangle(backBrush, e.Bounds);
            using var font = new Font(tabControl.Font, selected ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, page.Text, font, e.Bounds, selected ? Color.FromArgb(0, 90, 180) : Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // "Rect" draws (and persists — RectStore) on the Daily tab's own chart. "T-Line" arms BOTH
        // the Hora and 15 Min tabs at once (persists via TLineStore, tags "DailyHora"/"Daily15Min"
        // so they never mix with the live chart's own "1h"/"RTH" T-Lines on the same symbol), per
        // explicit request.
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 4) };
        var btnRect = new Button { Text = "Rect", Location = new Point(0, 2), Size = new Size(60, 24) };
        var btnColorRect = new Button { Text = "Color Rect", Location = new Point(66, 2), Size = new Size(80, 24) };
        var btnTLine = new Button { Text = "T-Line", Location = new Point(150, 2), Size = new Size(60, 24) };
        var btnHLine = new Button { Text = "H-Line", Location = new Point(216, 2), Size = new Size(60, 24) };
        btnRect.Click += async (s, e) =>
        {
            if (_webView.CoreWebView2 == null) return;
            var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleRect();");
            btnRect.BackColor = result == "true" ? Color.LightGray : SystemColors.Control;
        };
        // "Color Rect" — Daily tab only, same 2-click draw as "Rect" but filled red/green
        // depending on drag direction (see ColorRectPrimitive in chart.html), per explicit request.
        btnColorRect.Click += async (s, e) =>
        {
            if (_webView.CoreWebView2 == null) return;
            var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleColorRect();");
            btnColorRect.BackColor = result == "true" ? Color.LightSalmon : SystemColors.Control;
        };
        btnTLine.Click += async (s, e) =>
        {
            if (_hourlyWebView.CoreWebView2 == null || _fifteenWebView.CoreWebView2 == null) return;
            var result = await _hourlyWebView.CoreWebView2.ExecuteScriptAsync("toggleTLine();");
            await _fifteenWebView.CoreWebView2.ExecuteScriptAsync("toggleTLine();");
            btnTLine.BackColor = result == "true" ? Color.Orange : SystemColors.Control;
        };
        // "H-Line" — arms BOTH the Hora and 15 Min tabs at once (same convention as T-Line above),
        // but unlike T-Line the line itself is a SINGLE shared line: drawing/deleting it on either
        // tab mirrors it onto the other one right here (see HandleHLineMessage), then relays out to
        // this same symbol's Charts-tab panel 1 (1h) AND panel 2 (15m RTH) via
        // TwoPanelChartsControl.AttachDailyMirroring, per explicit request.
        btnHLine.Click += async (s, e) =>
        {
            if (_hourlyWebView.CoreWebView2 == null || _fifteenWebView.CoreWebView2 == null) return;
            var result = await _hourlyWebView.CoreWebView2.ExecuteScriptAsync("toggleHLine();");
            await _fifteenWebView.CoreWebView2.ExecuteScriptAsync("toggleHLine();");
            btnHLine.BackColor = result == "true" ? Color.Red : SystemColors.Control;
        };
        toolbar.Controls.Add(btnRect);
        toolbar.Controls.Add(btnColorRect);
        toolbar.Controls.Add(btnTLine);
        toolbar.Controls.Add(btnHLine);
        // chart.html auto-disarms each tool itself once the 2nd click completes a
        // rectangle/T-Line — reset the button color to match, same pattern the live chart uses.
        // H-Line is a single click-to-place (not 2-click), so chart.html never auto-disarms it —
        // it stays armed until clicked again, same as the live chart's own H-Line button.
        OnRectPlacedEvent += () => btnRect.BackColor = SystemColors.Control;
        OnColorRectPlacedEvent += () => btnColorRect.BackColor = SystemColors.Control;
        OnTLinePlacedEvent += () => btnTLine.BackColor = SystemColors.Control;

        // "SMA Watch" — Daily tab only. Unlike Rect/T-Line these aren't 2-click drawing tools:
        // clicking one directly toggles whether that SMA's live-price cross is being watched (see
        // SmaDailyWatchStore.cs + ChartPanel.EvaluateSmaCrossWatches, which does the actual
        // monitoring/Telegram push/event log — this window is just where you arm/disarm it and see
        // the marker). Stays armed until explicitly removed (this button again, or Delete on the
        // chart marker), independent of whether this window or the live chart is currently open.
        var smaWatchButtons = new Dictionary<int, Button>();
        var smaWatchButtonsInOrder = new List<Button>();
        foreach (var period in new[] { 20, 40, 100, 200 })
        {
            var btn = new Button { Size = new Size(60, 24), Text = $"SMA{period}" };
            smaWatchButtons[period] = btn;
            smaWatchButtonsInOrder.Add(btn);
            btn.Click += (s, e) =>
            {
                var armed = SmaDailyWatchStore.Load(_symbol).Contains(period);
                if (armed) SmaDailyWatchStore.Remove(_symbol, period);
                else SmaDailyWatchStore.Add(_symbol, period);
                btn.BackColor = armed ? SystemColors.Control : Color.LightYellow;
                OnSmaWatchChangedEvent?.Invoke(period, !armed);
                RefreshSmaWatchMarkersAsync();
            };
            toolbar.Controls.Add(btn);
        }
        _smaWatchButtons = smaWatchButtons;

        // Centered horizontally in the toolbar (instead of a fixed left offset right after
        // Rect/Color Rect/T-Line), per explicit request — clamped so it never creeps left of those
        // 3 buttons even on a narrow window. Re-run on every toolbar resize, same pattern as the
        // right-anchored D.PM/D40/D100/D200 group below.
        const int smaWatchGroupWidth = 4 * 60 + 3 * 6; // 4 buttons, 60px each, 6px gaps
        const int leftBoundary = 282; // right after btnHLine (216 + 60 + 6)
        void LayoutSmaWatchButtonsCentered()
        {
            var xStart = Math.Max(leftBoundary, (toolbar.ClientSize.Width - smaWatchGroupWidth) / 2);
            var bx = xStart;
            foreach (var btn in smaWatchButtonsInOrder)
            {
                btn.Location = new Point(bx, 2);
                bx += 66;
            }
        }
        toolbar.SizeChanged += (s, e) => LayoutSmaWatchButtonsCentered();
        LayoutSmaWatchButtonsCentered();

        // "D.PM" — controls whether the solid yellow Daily SMA20 reference line (ChartPanel.
        // EvaluateDailyPmAndBb) is drawn on panel 1/2 (tab Charts) and panel 3 (popup), per explicit
        // request. Persisted per symbol (tickers.json, same store as AWS/Telegram) — reflects the
        // stored state on open, toggling both saves and fires OnDailyPmLineToggledEvent so whichever
        // live panels are open react immediately instead of waiting for the next hourly close.
        var chkDailyPmLine = new CheckBox
        {
            Text     = "D.PM",
            AutoSize = true,
            Anchor   = AnchorStyles.Top | AnchorStyles.Right,
            Checked  = Form1.IsDailyPmLineEnabledFor(_symbol),
            ForeColor = Color.FromArgb(0xf5, 0xa6, 0x23) // matches chart.html's smaColors[20]
        };
        chkDailyPmLine.CheckedChanged += (s, e) =>
        {
            Form1.SetDailyPmLineEnabledFor(_symbol, chkDailyPmLine.Checked);
            OnDailyPmLineToggledEvent?.Invoke(chkDailyPmLine.Checked);
        };
        toolbar.Controls.Add(chkDailyPmLine);

        // "D40"/"D100"/"D200" — same idea as "D.PM" above but for the other Daily SMA periods, and
        // tab-Charts-only (panel 1/2, never panel 3), per explicit request. Independent checkboxes
        // (not a radio group) even though normally only one of these (plus D.PM) is shown at once.
        // Same smaColors map chart.html uses (kept in sync manually — no shared source between C#
        // and JS for this), so each checkbox's label reads as "this SMA's own color" at a glance.
        var smaColorsByPeriod = new Dictionary<int, Color>
        {
            [40]  = Color.FromArgb(0xef, 0x53, 0x50),
            [100] = Color.FromArgb(0x26, 0xa6, 0x9a),
            [200] = Color.FromArgb(0xa2, 0x59, 0xff)
        };
        var dailySmaCheckboxes = new List<CheckBox> { chkDailyPmLine };
        foreach (var period in new[] { 40, 100, 200 })
        {
            var chk = new CheckBox
            {
                Text     = $"D{period}",
                AutoSize = true,
                Anchor   = AnchorStyles.Top | AnchorStyles.Right,
                Checked  = Form1.GetDailySmaLinesEnabledFor(_symbol).Contains(period),
                ForeColor = smaColorsByPeriod[period]
            };
            chk.CheckedChanged += (s, e) =>
            {
                Form1.SetDailySmaLineEnabledFor(_symbol, period, chk.Checked);
                OnDailySmaLineToggledEvent?.Invoke(period, chk.Checked);
            };
            toolbar.Controls.Add(chk);
            dailySmaCheckboxes.Add(chk);
        }

        // Pinned to the toolbar's far right edge (D.PM, D40, D100, D200 left-to-right), Anchor=Right
        // keeps them there if the window is resized — positioned here (after all 4 are created, so
        // PreferredSize is known) and re-run on every toolbar resize.
        void LayoutDailySmaCheckboxesRight()
        {
            var xRight = toolbar.ClientSize.Width - toolbar.Padding.Right;
            for (int i = dailySmaCheckboxes.Count - 1; i >= 0; i--)
            {
                var w = dailySmaCheckboxes[i].PreferredSize.Width;
                xRight -= w;
                dailySmaCheckboxes[i].Location = new Point(xRight, 6);
                xRight -= 10;
            }
        }
        toolbar.SizeChanged += (s, e) => LayoutDailySmaCheckboxesRight();
        LayoutDailySmaCheckboxesRight();

        Controls.Add(tabControl);
        Controls.Add(toolbar);
        Load += async (s, e) => await InitAsync();
    }

    // Fired when the auto-disarming Rect/T-Line tool finishes placing one, so the toolbar button
    // color resets — same convention as MultiChartForm's own btnRect/btnTLine wiring.
    public event Action? OnRectPlacedEvent;
    public event Action? OnColorRectPlacedEvent;
    public event Action? OnTLinePlacedEvent;

    // Fired when a T-Line is drawn/deleted on the "Hora" (tag "DailyHora") or "15 Min" (tag
    // "Daily15Min") tab — MultiChartForm relays these onto the live 1h/RTH panel respectively
    // (ChartPanel.AddMirroredTLineAsync/RemoveMirroredTLineAsync), per explicit request that
    // drawings there replicate onto the live chart. One-way only (live -> Daily is NOT mirrored).
    public event Action<string, long, decimal, long, decimal>? OnTLineDrawnEvent;
    public event Action<string, long, decimal, long, decimal>? OnTLineDeletedEvent;

    // Fired when the H-Line is drawn/deleted on either the "Hora" or "15 Min" tab (it's a single
    // shared line, already mirrored between the two tabs right here — see HandleHLineMessage).
    // TwoPanelChartsControl.AttachDailyMirroring relays these onto BOTH the live 1h and 15m RTH
    // panels (ChartPanel.AddMirroredHLineAsync/RemoveHLineAsync), per explicit request. One-way
    // only (live -> Daily is NOT mirrored).
    public event Action<long, decimal>? OnHLineDrawnEvent;
    public event Action<decimal>? OnHLineDeletedEvent;

    // Fired when an "SMA Watch" toolbar button (or the chart marker's Delete) arms/disarms
    // monitoring for that period — MultiChartForm relays this to the live 1h panel
    // (ChartPanel.SetSmaCrossWatchAsync), which does the actual cross detection. Persisted by
    // SmaDailyWatchStore regardless of whether anything is listening at the moment.
    public event Action<int, bool>? OnSmaWatchChangedEvent;

    // Fired when the "D.PM" checkbox toggles — TwoPanelChartsControl/MultiChartForm listen so
    // whichever live panels are currently open show/hide the yellow Daily SMA20 line immediately.
    public event Action<bool>? OnDailyPmLineToggledEvent;

    // Fired when a "D40"/"D100"/"D200" checkbox toggles — TwoPanelChartsControl listens (tab
    // Charts only, panel 1/2 — never MultiChartForm/panel 3, per explicit request).
    public event Action<int, bool>? OnDailySmaLineToggledEvent;

    private async void RefreshSmaWatchMarkersAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        var periods = SmaDailyWatchStore.Load(_symbol);
        await _webView.CoreWebView2.ExecuteScriptAsync($"loadSmaWatches({JsonSerializer.Serialize(periods)});");
    }

    private async Task InitAsync()
    {
        await InitChartTabAsync(_webView, _dailyCandles, _dailyCandles.Count);
        _lastDailyCandle = _dailyCandles.Count > 0 ? _dailyCandles[^1] : null;
        await EvaluatePisoTechoAsync();

        // Blue "current price" line, per explicit request — same primitive the 1h/15m RTH panels'
        // premarket line uses, just anchored on the Daily chart's still-forming "today" bar instead.
        // Armed here unconditionally; MultiChartForm feeds it the live spot via UpdateLivePrice as
        // ticks arrive (see OnLiveTick relay) — before the first tick lands, it just stays hidden.
        await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");

        // "SMA Watch" persistence — replay whatever's currently armed (button highlight + chart
        // marker), then listen for deletions via the chart marker's Delete key.
        var armedSmaWatches = SmaDailyWatchStore.Load(_symbol);
        foreach (var period in armedSmaWatches)
            if (_smaWatchButtons.TryGetValue(period, out var btn)) btn.BackColor = Color.LightYellow;
        await _webView.CoreWebView2.ExecuteScriptAsync($"loadSmaWatches({JsonSerializer.Serialize(armedSmaWatches)});");
        _webView.CoreWebView2.WebMessageReceived += (s, e) => HandleSmaWatchMessage(e);

        // "Rect" tool persistence (RectStore, tag "Daily") — replay whatever was drawn in a
        // previous session, then listen for new/deleted ones from now on.
        var savedRects = RectStore.Load(_symbol, "Daily");
        var rectsJson = JsonSerializer.Serialize(savedRects.Select(r => new { t1 = r.T1, p1 = r.P1, t2 = r.T2, p2 = r.P2 }));
        await _webView.CoreWebView2.ExecuteScriptAsync($"loadRects({rectsJson});");
        _webView.CoreWebView2.WebMessageReceived += (s, e) => HandleRectMessage(e, "Daily", () => OnRectPlacedEvent?.Invoke());

        // "Color Rect" tool persistence — same RectStore, separate tag ("DailyColor") so it never
        // mixes with the plain gray Rect tool above. Color itself is derived from p1 vs p2 at draw
        // time (see ColorRectPrimitive), so nothing extra needs storing.
        var savedColorRects = RectStore.Load(_symbol, "DailyColor");
        var colorRectsJson = JsonSerializer.Serialize(savedColorRects.Select(r => new { t1 = r.T1, p1 = r.P1, t2 = r.T2, p2 = r.P2 }));
        await _webView.CoreWebView2.ExecuteScriptAsync($"loadColorRects({colorRectsJson});");
        _webView.CoreWebView2.WebMessageReceived += (s, e) => HandleColorRectMessage(e, () => OnColorRectPlacedEvent?.Invoke());

        // "Hora"/"15 Min" tabs — same chart (candles + SMA20/40/100/200 + Bollinger), just at
        // those two timeframes instead of Daily, per explicit request. "Hora" reuses the same
        // persisted hourly history GetLastDailyCandles itself aggregates from (HourlyCandleStore) —
        // no extra fetch needed. "15 Min" has no persisted store, so it's a fresh REST history
        // fetch + RTH-only aggregation, same call ChartPanel.LoadHistoryAsync makes for its own
        // 15m RTH panel.
        var hourlyCandles = HourlyCandleStore.Load(_symbol);
        await InitChartTabAsync(_hourlyWebView, hourlyCandles, 20);
        _lastHourlyCandle = hourlyCandles.Count > 0 ? hourlyCandles[^1] : null;
        await LoadAndWireTLinesAsync(_hourlyWebView, "DailyHora");

        // Blue "current price" line, per explicit request — same primitive as the Daily tab's own,
        // but anchored at today's actual session-open time (9:30 AM ET) instead of "today's still-
        // forming bar", matching the live 1h panel's own convention (ChartPanel.
        // GetTodaySessionOpenFakeEpoch) since this tab shows real hourly bars, not one-bar-per-day.
        // Fed the live spot the same way as the Daily tab's line — see UpdateLivePrice.
        await _hourlyWebView.CoreWebView2!.ExecuteScriptAsync($"startPreMarketLine({GetTodaySessionOpenFakeEpoch()});");

        // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day (same
        // constraint ChartPanel.LoadHistoryAsync works around) — request 10 (the closest valid
        // value at/above 8) so there's enough loaded for the 8-day initial zoom.
        var history = await _historyClient.GetHistoricalCandlesAsync(_symbol, 10);
        var filtered = CandleAggregation.FilterSession(history, rthOnly: true);
        var fifteenCandles = CandleAggregation.AggregateToInterval(filtered, 15, rthOnly: true);
        await InitChartTabAsync(_fifteenWebView, fifteenCandles, 8, showSmas: false, bollingerMiddleSolid: true);
        _lastFifteenCandle = fifteenCandles.Count > 0 ? fifteenCandles[^1] : null;
        await LoadAndWireTLinesAsync(_fifteenWebView, "Daily15Min");

        // Blue "current price" line, per explicit request — same session-open anchor as the Hora
        // tab's own line (ChartPanel.GetTodaySessionOpenFakeEpoch convention), since this tab also
        // shows real (15-minute) intraday bars, not one bar per day. Fed by UpdateLivePrice below.
        await _fifteenWebView.CoreWebView2!.ExecuteScriptAsync($"startPreMarketLine({GetTodaySessionOpenFakeEpoch()});");

        await LoadAndWireHLinesAsync();
    }

    // "T-Line" tool persistence (TLineStore) for one of the Hora/15 Min tabs — replay whatever was
    // drawn in a previous session, then listen for new/deleted ones from now on.
    private async Task LoadAndWireTLinesAsync(WebView2 webView, string tag)
    {
        if (webView.CoreWebView2 == null) return;
        var savedLines = TLineStore.Load(_symbol, tag);
        var linesJson = JsonSerializer.Serialize(savedLines.Select(l => new { t1 = l.T1, p1 = l.P1, t2 = l.T2, p2 = l.P2 }));
        await webView.CoreWebView2.ExecuteScriptAsync($"loadTLines({linesJson});");
        webView.CoreWebView2.WebMessageReceived += (s, e) => HandleTLineMessage(e, tag, () => OnTLinePlacedEvent?.Invoke());
    }

    // "H-Line" tool persistence (HLineStore, one shared line for both tabs) — replay whatever was
    // drawn in a previous session onto BOTH tabs, then listen for new/deleted ones on each.
    private async Task LoadAndWireHLinesAsync()
    {
        var saved = HLineStore.Load(_symbol);
        foreach (var (time, price) in saved)
        {
            var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_hourlyWebView.CoreWebView2 != null)
                await _hourlyWebView.CoreWebView2.ExecuteScriptAsync($"addMirroredHLine({time}, {priceStr});");
            if (_fifteenWebView.CoreWebView2 != null)
                await _fifteenWebView.CoreWebView2.ExecuteScriptAsync($"addMirroredHLine({time}, {priceStr});");
        }

        if (_hourlyWebView.CoreWebView2 != null)
            _hourlyWebView.CoreWebView2.WebMessageReceived += (s, e) => HandleHLineMessage(e, _fifteenWebView);
        if (_fifteenWebView.CoreWebView2 != null)
            _fifteenWebView.CoreWebView2.WebMessageReceived += (s, e) => HandleHLineMessage(e, _hourlyWebView);
    }

    // otherWebView is whichever of Hora/15 Min did NOT originate this message — the line gets
    // mirrored there too (addMirroredHLine/removeHLine never postMessage back out, so this can't
    // ping-pong), then relayed out to the live Charts-tab panels via OnHLineDrawnEvent/
    // OnHLineDeletedEvent.
    private void HandleHLineMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e, WebView2 otherWebView)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (type != "hline_add" && type != "hline_delete") return;

            if (type == "hline_add")
            {
                var t = root.GetProperty("time").GetInt64();
                var p = root.GetProperty("price").GetDecimal();
                HLineStore.Append(_symbol, t, p);
                var priceStr = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _ = otherWebView.CoreWebView2?.ExecuteScriptAsync($"addMirroredHLine({t}, {priceStr});");
                OnHLineDrawnEvent?.Invoke(t, p);
            }
            else
            {
                var p = root.GetProperty("price").GetDecimal();
                HLineStore.Remove(_symbol, p);
                var priceStr = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _ = otherWebView.CoreWebView2?.ExecuteScriptAsync($"removeHLine({priceStr});");
                OnHLineDeletedEvent?.Invoke(p);
            }
        }
        catch
        {
            // Best-effort — never let a malformed message crash the window.
        }
    }

    private void HandleRectMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e, string contextTag, Action onPlaced)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (type != "bluerect_add" && type != "bluerect_delete" && type != "rect_placed") return;

            if (type == "rect_placed") { onPlaced(); return; }

            var t1 = root.GetProperty("t1").GetInt64();
            var p1 = root.GetProperty("p1").GetDecimal();
            var t2 = root.GetProperty("t2").GetInt64();
            var p2 = root.GetProperty("p2").GetDecimal();
            if (type == "bluerect_add") RectStore.Append(_symbol, contextTag, t1, p1, t2, p2);
            else RectStore.Remove(_symbol, contextTag, t1, p1, t2, p2);
        }
        catch
        {
            // Best-effort — never let a malformed message crash the window.
        }
    }

    // "Color Rect" tool — same RectStore, own tag "DailyColor" and own message-type prefix
    // ("colorrect_*") so it never collides with the plain gray Rect tool's "bluerect_*"/"rect_*"
    // messages on the same WebMessageReceived stream.
    private void HandleColorRectMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e, Action onPlaced)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (type != "colorrect_add" && type != "colorrect_delete" && type != "colorrect_placed") return;

            if (type == "colorrect_placed") { onPlaced(); return; }

            var t1 = root.GetProperty("t1").GetInt64();
            var p1 = root.GetProperty("p1").GetDecimal();
            var t2 = root.GetProperty("t2").GetInt64();
            var p2 = root.GetProperty("p2").GetDecimal();
            if (type == "colorrect_add") RectStore.Append(_symbol, "DailyColor", t1, p1, t2, p2);
            else RectStore.Remove(_symbol, "DailyColor", t1, p1, t2, p2);
        }
        catch
        {
            // Best-effort — never let a malformed message crash the window.
        }
    }

    // Deleting the chart marker (Delete key on it) disarms the watch — same effect as clicking
    // its toolbar button again, just reachable from the chart itself.
    private void HandleSmaWatchMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "smawatch_delete") return;

            var period = root.GetProperty("period").GetInt32();
            SmaDailyWatchStore.Remove(_symbol, period);
            if (_smaWatchButtons.TryGetValue(period, out var btn)) btn.BackColor = SystemColors.Control;
            OnSmaWatchChangedEvent?.Invoke(period, false);
        }
        catch
        {
            // Best-effort — never let a malformed message crash the window.
        }
    }

    private void HandleTLineMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e, string tag, Action onPlaced)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (type != "tline" && type != "tline_delete" && type != "tline_placed") return;

            if (type == "tline_placed") { onPlaced(); return; }

            var t1 = root.GetProperty("t1").GetInt64();
            var p1 = root.GetProperty("p1").GetDecimal();
            var t2 = root.GetProperty("t2").GetInt64();
            var p2 = root.GetProperty("p2").GetDecimal();
            if (type == "tline")
            {
                TLineStore.Append(_symbol, tag, t1, p1, t2, p2);
                OnTLineDrawnEvent?.Invoke(tag, t1, p1, t2, p2);
            }
            else
            {
                TLineStore.Remove(_symbol, tag, t1, p1, t2, p2);
                OnTLineDeletedEvent?.Invoke(tag, t1, p1, t2, p2);
            }
        }
        catch
        {
            // Best-effort — never let a malformed message crash the window.
        }
    }

    // Shared setup for each tab's own WebView2 — navigate to chart.html, configure Bollinger (and
    // optionally SMAs — "15 Min" shows Bollinger only, per explicit request; Daily and Hora show
    // both), load the given candle history. visibleDays is a day COUNT for Hora/15 Min (matches the
    // live panels' own convention: configureVisibleDays groups by calendar day regardless of candle
    // interval) but simply equals the bar count for Daily, where each bar IS one day.
    private static async Task InitChartTabAsync(WebView2 webView, List<CandleData> candles, int visibleDays, bool showSmas = true, bool bollingerMiddleSolid = false)
    {
        await webView.EnsureCoreWebView2Async();

        var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
        var navDone = new TaskCompletionSource();
        webView.CoreWebView2.NavigationCompleted += (s, args) =>
        {
            if (args.IsSuccess) navDone.TrySetResult();
        };

        // Same cache-busting query string ChartPanel.LoadHistoryAsync uses — forces a fresh read
        // of chart.html instead of a stale cached copy from an earlier window this session.
        var chartUri = new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}";
        webView.CoreWebView2.Navigate(chartUri);
        await navDone.Task;

        if (showSmas) await webView.CoreWebView2.ExecuteScriptAsync("configureSmas([20,40,100,200]);");
        // "15 Min" solid — same "PM" (Punto Medio / SMA20) convention the live 15m RTH panel uses
        // (ChartPanel: "Middle band (SMA20) drawn solid here, vs dashed on the 1h panel"), per
        // explicit request; Daily/Hora keep it dashed (their default SMA20 line already covers it).
        await webView.CoreWebView2.ExecuteScriptAsync($"configureBollinger(20, 2, {(bollingerMiddleSolid ? "true" : "false")});");
        await webView.CoreWebView2.ExecuteScriptAsync($"configureVisibleDays({visibleDays});");

        var json = ChartPanel.ToChartJsonPublic(candles);
        await webView.CoreWebView2.ExecuteScriptAsync($"loadHistory({json});");
    }

    // Ported from ChartPanel.EvaluatePisoTechoPair/EvaluateSingleSmaPisoTecho — SAME criterion
    // panel 1 uses (fast/slow SMA pairs (20,40) and (100,200), bearish alignment -> that SMA is
    // "Techo" only while price stays below it, bullish -> "Piso" only while price stays above it),
    // just evaluated against the Daily series' last CLOSED bar instead of the 1h panel's own
    // _closedCandles — if the most recent daily candle is TODAY's (still forming, market open),
    // it's excluded so this reads exactly like panel 1's own pre-market snapshot, not a live value
    // that would flicker as today's still-forming daily bar moves.
    private async Task EvaluatePisoTechoAsync()
    {
        var closed = _dailyCandles;
        if (closed.Count > 0)
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var todayEastern = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern));
            var lastBarDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(closed[^1].Time, eastern));
            if (lastBarDate >= todayEastern) closed = closed[..^1];
        }

        (string? Fast, string? Slow) EvaluatePair(int fastPeriod, int slowPeriod)
        {
            var fast = Sma(closed, fastPeriod);
            var slow = Sma(closed, slowPeriod);
            if (fast == null || slow == null || fast == slow) return (null, null);

            var price = closed[^1].Close;
            var bearish = fast < slow;
            return (EvaluateSingleSmaPisoTecho(fast.Value, price, bearish), EvaluateSingleSmaPisoTecho(slow.Value, price, bearish));
        }

        var (r20, r40) = EvaluatePair(20, 40);
        var (r100, r200) = EvaluatePair(100, 200);

        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(20, {ToJsStringOrNull(r20)}, 40, {ToJsStringOrNull(r40)});");
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(100, {ToJsStringOrNull(r100)}, 200, {ToJsStringOrNull(r200)});");
    }

    private static decimal? Sma(List<CandleData> closed, int period)
    {
        if (closed.Count < period) return null;
        decimal sum = 0;
        for (int i = closed.Count - period; i < closed.Count; i++) sum += closed[i].Close;
        return sum / period;
    }

    private static string? EvaluateSingleSmaPisoTecho(decimal sma, decimal price, bool bearishAlignment) =>
        bearishAlignment ? (price < sma ? "Techo" : null) : (price > sma ? "Piso" : null);

    private static string ToJsStringOrNull(string? value) => value == null ? "null" : $"'{value}'";

    // Fed by MultiChartForm (hourlyPanel.OnLiveTick relay) — updates the blue "current price" line
    // to today's live spot, whether that's a premarket tick or a live RTH price. Also now updates
    // the actual still-forming candle on each tab (per explicit request — this popup used to only
    // ever show fully-CLOSED days/hours/15-min bars, so you couldn't see the in-progress one
    // visually approaching a Piso/Techo level in real time). No-op once this window is
    // closed/disposed.
    public async Task UpdateLivePrice(decimal price)
    {
        if (IsDisposed) return;
        var priceArg = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_webView.CoreWebView2 != null)
            await _webView.CoreWebView2.ExecuteScriptAsync($"updatePreMarketLine({priceArg}, null);");
        if (_hourlyWebView.CoreWebView2 != null)
            await _hourlyWebView.CoreWebView2.ExecuteScriptAsync($"updatePreMarketLine({priceArg}, null);");
        if (_fifteenWebView.CoreWebView2 != null)
            await _fifteenWebView.CoreWebView2.ExecuteScriptAsync($"updatePreMarketLine({priceArg}, null);");

        var nowUtc = DateTime.UtcNow;

        // Daily: the bucket itself never changes mid-session (a new day only starts if this
        // window is left open across midnight, not worth handling), so just extend the existing
        // bar's High/Low/Close in place.
        if (_lastDailyCandle != null && _webView.CoreWebView2 != null)
        {
            _lastDailyCandle.High  = Math.Max(_lastDailyCandle.High, price);
            _lastDailyCandle.Low   = Math.Min(_lastDailyCandle.Low, price);
            _lastDailyCandle.Close = price;
            await _webView.CoreWebView2.ExecuteScriptAsync($"updateLastCandle({ChartPanel.ToChartJsonPublic(_lastDailyCandle)});");
        }

        // Hora/15 Min: the bucket DOES change during a session (every hour / every 15 min), so
        // check whether the live tick still belongs to the currently-tracked bar or starts a new
        // one — same bucket-start convention AggregateToHourlyRthBuckets/AggregateToInterval use,
        // so a later full reload always agrees with what was shown live.
        if (_hourlyWebView.CoreWebView2 != null)
        {
            var bucketStart = CandleAggregation.HourlyRthBucketStartUtc(nowUtc);
            if (_lastHourlyCandle != null && _lastHourlyCandle.Time == bucketStart)
            {
                _lastHourlyCandle.High  = Math.Max(_lastHourlyCandle.High, price);
                _lastHourlyCandle.Low   = Math.Min(_lastHourlyCandle.Low, price);
                _lastHourlyCandle.Close = price;
            }
            else
            {
                _lastHourlyCandle = new CandleData { Time = bucketStart, Open = price, High = price, Low = price, Close = price };
            }
            await _hourlyWebView.CoreWebView2.ExecuteScriptAsync($"updateLastCandle({ChartPanel.ToChartJsonPublic(_lastHourlyCandle)});");
        }

        if (_fifteenWebView.CoreWebView2 != null)
        {
            var bucketStart = CandleAggregation.FifteenMinRthBucketStartUtc(nowUtc);
            if (_lastFifteenCandle != null && _lastFifteenCandle.Time == bucketStart)
            {
                _lastFifteenCandle.High  = Math.Max(_lastFifteenCandle.High, price);
                _lastFifteenCandle.Low   = Math.Min(_lastFifteenCandle.Low, price);
                _lastFifteenCandle.Close = price;
            }
            else
            {
                _lastFifteenCandle = new CandleData { Time = bucketStart, Open = price, High = price, Low = price, Close = price };
            }
            await _fifteenWebView.CoreWebView2.ExecuteScriptAsync($"updateLastCandle({ChartPanel.ToChartJsonPublic(_lastFifteenCandle)});");
        }
    }

    // Today's 9:30 AM ET (RTH session open), same "ET digits disguised as UTC" fake epoch every
    // other reference line's anchor uses — see ChartPanel.GetTodaySessionOpenFakeEpoch, which this
    // mirrors exactly for the Hora tab's own blue price line.
    private long GetTodaySessionOpenFakeEpoch()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern).Date;
        var sessionOpenEastern = todayEastern.AddHours(9).AddMinutes(30);
        var sessionOpenUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionOpenEastern, DateTimeKind.Unspecified), eastern);
        return ChartPanel.FakeUtcEpochSeconds(sessionOpenUtc);
    }
}
