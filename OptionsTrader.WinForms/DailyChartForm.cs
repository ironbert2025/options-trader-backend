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
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _hourlyWebView = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _fifteenWebView = new() { Dock = DockStyle.Fill };
    private readonly string _symbol;
    private readonly List<CandleData> _dailyCandles;
    private readonly SchwabStreamerClient _historyClient;

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
        toolbar.Controls.Add(btnRect);
        toolbar.Controls.Add(btnColorRect);
        toolbar.Controls.Add(btnTLine);
        // chart.html auto-disarms each tool itself once the 2nd click completes a
        // rectangle/T-Line — reset the button color to match, same pattern the live chart uses.
        OnRectPlacedEvent += () => btnRect.BackColor = SystemColors.Control;
        OnColorRectPlacedEvent += () => btnColorRect.BackColor = SystemColors.Control;
        OnTLinePlacedEvent += () => btnTLine.BackColor = SystemColors.Control;

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

    private async Task InitAsync()
    {
        await InitChartTabAsync(_webView, _dailyCandles, _dailyCandles.Count);
        await EvaluatePisoTechoAsync();

        // Blue "current price" line, per explicit request — same primitive the 1h/15m RTH panels'
        // premarket line uses, just anchored on the Daily chart's still-forming "today" bar instead.
        // Armed here unconditionally; MultiChartForm feeds it the live spot via UpdateLivePrice as
        // ticks arrive (see OnLiveTick relay) — before the first tick lands, it just stays hidden.
        await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");

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
        await InitChartTabAsync(_hourlyWebView, hourlyCandles, 20, showSmas: false);
        await LoadAndWireTLinesAsync(_hourlyWebView, "DailyHora");

        // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day (same
        // constraint ChartPanel.LoadHistoryAsync works around) — request 10 (the closest valid
        // value at/above 8) so there's enough loaded for the 8-day initial zoom.
        var history = await _historyClient.GetHistoricalCandlesAsync(_symbol, 10);
        var filtered = CandleAggregation.FilterSession(history, rthOnly: true);
        var fifteenCandles = CandleAggregation.AggregateToInterval(filtered, 15, rthOnly: true);
        await InitChartTabAsync(_fifteenWebView, fifteenCandles, 8, showSmas: false);
        await LoadAndWireTLinesAsync(_fifteenWebView, "Daily15Min");
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
    // optionally SMAs — "Hora"/"15 Min" show Bollinger only, per explicit request; Daily keeps
    // both), load the given candle history. visibleDays is a day COUNT for Hora/15 Min (matches the
    // live panels' own convention: configureVisibleDays groups by calendar day regardless of candle
    // interval) but simply equals the bar count for Daily, where each bar IS one day.
    private static async Task InitChartTabAsync(WebView2 webView, List<CandleData> candles, int visibleDays, bool showSmas = true)
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
        await webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");
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
    // to today's live spot, whether that's a premarket tick or a live RTH price. No-op once this
    // window is closed/disposed.
    public async Task UpdateLivePrice(decimal price)
    {
        if (IsDisposed || _webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"updatePreMarketLine({price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, null);");
    }
}
