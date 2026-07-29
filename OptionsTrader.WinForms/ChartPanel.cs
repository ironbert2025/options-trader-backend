using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Which candle interval and session window a given ChartPanel shows.
public enum ChartPanelMode
{
    Hourly15,       // 1h candles, regular session only (9:30 AM - 4:00 PM ET)
    Fifteen_RTH,    // 15m candles, regular session only
    Fifteen_Full    // 15m candles, regular session + pre/after-hours (whatever Schwab returns)
}

// One WebView2-hosted candlestick chart. Does NOT own a streaming connection — it's handed a
// SchwabStreamerClient for one-off REST history fetches (GetHistoricalCandlesAsync, no per-
// account limit on that) and a separate ICandleFeed for live ticks. In this app instance's own
// process the live feed might be that SAME SchwabStreamerClient (if this instance is the "hub"
// that owns the one Schwab streaming connection allowed per account) or a CandleHubClient
// relaying another instance's connection over localhost (if it isn't) — ChartPanel doesn't need
// to know which.
public class ChartPanel : Panel
{
    private readonly string _symbol;
    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly ChartPanelMode _mode;
    private int _intervalMinutes; // mutable only for Fifteen_Full, via ToggleIntervalAsync (5m <-> 15m)
    private readonly bool _rthOnly;
    private readonly Label _header;
    private WebView2 _webView = null!;
    private bool _closing;

    // The session-filtered 1-minute candles from the last historical fetch, cached so
    // ToggleIntervalAsync can re-aggregate at a different interval without re-fetching from Schwab.
    private List<CandleData> _rawHistory = new();

    // The bucket currently being built from live 1-min ticks, and which bucket index it belongs
    // to (so we know when a new tick starts a new bucket vs. extends the current one).
    private CandleData? _liveBucket;
    private int? _liveBucketIndex;
    private DateTime _liveAnchor;

    // Cross-SMA monitoring (Hourly15 panel only) — closed 1h candles kept for computing SMA
    // ourselves in C# (same simple-average formula as the JS overlay), and which (period, up)
    // combinations are currently armed to push to Telegram on a genuine crossover.
    private readonly List<CandleData> _closedCandles = new();
    private readonly HashSet<(int Period, bool Up)> _armedCrossMonitors = new();
    private static readonly Dictionary<int, string> SmaColorNames = new()
    {
        [20] = "Yellow", [40] = "Red", [100] = "Green", [200] = "Purple"
    };

    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public ChartPanel(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, ChartPanelMode mode)
    {
        _symbol        = symbol;
        _historyClient = historyClient;
        _liveFeed      = liveFeed;
        _mode          = mode;
        (_intervalMinutes, _rthOnly) = mode switch
        {
            ChartPanelMode.Hourly15     => (60, true),
            ChartPanelMode.Fifteen_RTH  => (15, true),
            ChartPanelMode.Fifteen_Full => (15, false),
            _ => (60, true)
        };

        _header = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(19, 23, 34),
            Text      = $"{symbol} — {ModeLabel(mode)}"
        };

        InitializeWebView();

        Controls.Add(_webView);
        Controls.Add(_header);

        _liveFeed.OnNewCandle     += Streamer_OnNewCandle;
        _liveFeed.OnLevelOneTick  += Streamer_OnLevelOneTick;
        _liveFeed.OnDisconnected  += Streamer_OnDisconnected;

        HandleCreated += async (s, e) => await LoadHistoryAsync();
        Disposed += (s, e) =>
        {
            _closing = true;
            _liveFeed.OnNewCandle    -= Streamer_OnNewCandle;
            _liveFeed.OnLevelOneTick -= Streamer_OnLevelOneTick;
            _liveFeed.OnDisconnected -= Streamer_OnDisconnected;
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        };
    }

    private static string ModeLabel(ChartPanelMode mode) => mode switch
    {
        ChartPanelMode.Hourly15     => "1h",
        ChartPanelMode.Fifteen_RTH  => "15m RTH",
        ChartPanelMode.Fifteen_Full => "15m RTH+Overnight",
        _ => mode.ToString()
    };

    // Renders this panel's actual chart content via the WebView2 engine itself — NOT a screen
    // capture — so it works even if the window is minimized, occluded, or off-screen. Used to
    // build the combined 3-chart trade snapshot in MultiChartForm.
    public async Task<Bitmap> CaptureImageAsync()
    {
        using var stream = new MemoryStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private void InitializeWebView()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
    }

    // Toggles DZ/SZ drawing mode on/off. While on, every pair of clicks on this panel's chart
    // draws a new demand (green) + supply (red) line pair — keeps going until toggled off.
    // Called from the "DZ/SZ" toolbar button in MultiChartForm. Returns the new on/off state.
    public async Task<bool> ToggleDzSzModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarDZSZ();");
        return result == "true";
    }

    // Toggles Rect drawing mode on/off. While on, every pair of clicks draws a new sky-blue
    // rectangle between them (opposite corners, no value labels). Same toggle pattern as DZ/SZ.
    public async Task<bool> ToggleRectModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarRect();");
        return result == "true";
    }

    // Toggles the 1h panel's gray Rect tool on/off — same 2-click draw as Rect, but filled gray
    // (marking sideways/consolidation ranges) and each rectangle can be selected by clicking its
    // border and removed with the Delete key, independent of whether the tool is armed.
    public async Task<bool> ToggleRectGrisModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarRectGris();");
        return result == "true";
    }

    // Toggles T-Line drawing mode on/off. While on, every pair of clicks draws a new orange line
    // segment between them (not extended to the chart edges). Same toggle pattern as Rect.
    public async Task<bool> ToggleTLineModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarTLine();");
        return result == "true";
    }

    // Toggles H-Line drawing mode on/off. While on, every click draws a new red horizontal line
    // from the click point to the right edge of the chart. Same toggle pattern as DZ/SZ.
    public async Task<bool> ToggleHLineModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarHLine();");
        return result == "true";
    }

    // Toggles Arrow drawing mode on/off. While on, every pair of clicks draws a line + arrowhead
    // between them — red if the 1st click is above (higher price than) the 2nd, green otherwise.
    // Same toggle pattern as Rect/T-Line.
    public async Task<bool> ToggleArrowModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarArrow();");
        return result == "true";
    }

    // Toggles Piso/Techo text-label drawing mode on/off. While on, every click writes the given
    // orange text at that point (no pairing — one click per label). Same toggle pattern as H-Line.
    public async Task<bool> TogglePisoModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarPiso();");
        return result == "true";
    }

    public async Task<bool> ToggleTechoModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarTecho();");
        return result == "true";
    }

    // Toggles the 1h panel's vertical arrow tools on/off. While on, every click places a
    // fixed-length vertical arrow with its tip at the clicked point — green points up, red points
    // down. Selectable by clicking the shaft and removable with Delete, same as the gray Rect
    // tool.
    public async Task<bool> ToggleFlechaVerdeModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarFlechaVerde();");
        return result == "true";
    }

    public async Task<bool> ToggleFlechaRojaModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarFlechaRoja();");
        return result == "true";
    }

    // Clears every DZ/SZ pair, rectangle, T-Line, H-Line, Arrow and Piso/Techo label drawn on
    // this panel, and turns all drawing modes off. Also wipes the persisted T-Line/vertical-arrow
    // files for this symbol (1h panel only) — a real "clear" should clear what's saved too.
    public async Task ClearDrawingsAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("limpiarDibujos();");
        if (_mode == ChartPanelMode.Hourly15)
        {
            TLineStore.Clear(_symbol);
            VerticalArrowStore.Clear(_symbol);
        }
    }

    // Receives T-Line and vertical-arrow events from the 1h panel (window.chrome.webview.
    // postMessage from chart.html) and keeps TLineStore/VerticalArrowStore in sync with whatever
    // is actually on screen: a new one drawn gets appended, one deleted via the Delete key gets
    // removed, and a dragged arrow gets its stored position updated.
    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            switch (type)
            {
                case "tline":
                case "tline_delete":
                {
                    var t1 = root.GetProperty("t1").GetInt64();
                    var p1 = root.GetProperty("p1").GetDecimal();
                    var t2 = root.GetProperty("t2").GetInt64();
                    var p2 = root.GetProperty("p2").GetDecimal();
                    if (type == "tline") TLineStore.Append(_symbol, t1, p1, t2, p2);
                    else TLineStore.Remove(_symbol, t1, p1, t2, p2);
                    break;
                }
                case "arrow_add":
                case "arrow_delete":
                {
                    var time = root.GetProperty("time").GetInt64();
                    var price = root.GetProperty("price").GetDecimal();
                    var up = root.GetProperty("up").GetBoolean();
                    if (type == "arrow_add") VerticalArrowStore.Append(_symbol, time, price, up);
                    else VerticalArrowStore.Remove(_symbol, time, price, up);
                    break;
                }
                case "arrow_move":
                {
                    var oldTime = root.GetProperty("oldTime").GetInt64();
                    var oldPrice = root.GetProperty("oldPrice").GetDecimal();
                    var up = root.GetProperty("up").GetBoolean();
                    var newTime = root.GetProperty("newTime").GetInt64();
                    var newPrice = root.GetProperty("newPrice").GetDecimal();
                    VerticalArrowStore.Move(_symbol, oldTime, oldPrice, up, newTime, newPrice);
                    break;
                }
            }
        }
        catch
        {
            // Malformed/unexpected message from the page — ignore, not fatal.
        }
    }

    // Toggles this panel's candle interval between 15m and 5m — only meaningful for Fifteen_Full
    // (the only mode MultiChartForm wires this up for). Re-aggregates the cached 1-minute history
    // (no re-fetch from Schwab needed) at the new interval and reloads the chart + re-seeds the
    // live bucket aggregator. Returns true if now showing 5m candles.
    public async Task<bool> ToggleIntervalAsync()
    {
        _intervalMinutes = _intervalMinutes == 5 ? 15 : 5;
        _header.Text = $"{_symbol} — {_intervalMinutes}m RTH+Overnight";

        if (_webView.CoreWebView2 != null && _rawHistory.Count > 0)
        {
            var aggregated = AggregateToInterval(_rawHistory, _intervalMinutes, _rthOnly);
            if (aggregated.Count > 0)
            {
                await RunScriptAsync("cargarHistorial", aggregated);
                var last = aggregated[^1];
                _liveAnchor      = BucketAnchor(new[] { last }, _rthOnly);
                _liveBucketIndex = BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
                _liveBucket      = last;
            }
        }

        return _intervalMinutes == 5;
    }

    // Toggles the 1h panel between Daily and Hourly candles. All the aggregation (grouping the
    // already-loaded hourly history into one bar per day) and SMA recomputation happens entirely
    // in JS (chart.html's activarDaily) off the same data already on the chart — no new fetch or
    // re-seed needed here, unlike ToggleIntervalAsync above. Drawings (T-Line, arrows, etc.) are
    // untouched since they're anchored to real timestamps valid in either view. Returns true if
    // now showing Daily candles.
    public async Task<bool> ToggleDailyModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("activarDaily();");
        return result == "true";
    }

    // Toggles a "Cross UP/DOWN SMA(period)" monitor on/off. While armed, every 1h candle that
    // closes and forms a genuine crossover of that SMA triggers a chart capture + Telegram push.
    // Returns the new on/off state.
    public bool ToggleCrossMonitor(int period, bool up)
    {
        var key = (period, up);
        if (!_armedCrossMonitors.Remove(key))
        {
            _armedCrossMonitors.Add(key);
            return true;
        }
        return false;
    }

    // Captures this panel's chart as a PNG (via WebView2's native preview capture — pixel-exact,
    // doesn't depend on the window being visible/on top, unlike a screen-coordinate capture) and
    // pushes it to the configured Telegram channel.
    private async Task<(bool Ok, string Detail)> SendChartToTelegramAsync(string caption)
    {
        if (_webView.CoreWebView2 == null) return (false, "Chart not loaded yet.");

        try
        {
            var folder = @"C:\OptionsTraderPush";
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{_symbol}_{ModeLabel(_mode)}_{DateTime.Now:yyyyMMdd_HHmmss}.png".Replace(' ', '_'));

            using (var stream = File.Create(path))
            {
                await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }

            var (botToken, chatId) = TelegramSettingsStore.Load();
            var (ok, detail, _) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, caption);
            return (ok, detail);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // Evaluates every armed Cross monitor against the candle that just closed. "Genuine crossover"
    // means: candle color matches the direction (green for UP, red for DOWN), its close ends up on
    // the crossed side of the SMA(period) computed as of this candle, AND the previous candle was
    // still on the other side (or exactly on the line) — so it only fires once per actual cross,
    // not on every candle that happens to stay above/below the SMA.
    private void EvaluateCrossings(CandleData justClosed)
    {
        if (_armedCrossMonitors.Count == 0) return;

        foreach (var (period, up) in _armedCrossMonitors.ToList())
        {
            if (_closedCandles.Count < period + 1) continue; // not enough history for this + the prior SMA

            var currentSma  = Sma(period, _closedCandles.Count - 1);
            var previousSma = Sma(period, _closedCandles.Count - 2);
            if (currentSma == null || previousSma == null) continue;

            var previousClose = _closedCandles[^2].Close;
            var isGreen = justClosed.Close > justClosed.Open;
            var isRed   = justClosed.Close < justClosed.Open;

            var crossed = up
                ? isGreen && justClosed.Close > currentSma && previousClose <= previousSma
                : isRed   && justClosed.Close < currentSma && previousClose >= previousSma;

            if (!crossed) continue;

            var direction = up ? "UP" : "DOWN";
            var colorName = SmaColorNames.TryGetValue(period, out var c) ? c : string.Empty;
            var caption = $"Crossing {direction} SMA {period}({colorName})";
            _ = SendChartToTelegramAsync(caption);
        }
    }

    // Simple moving average of Close over the `period` candles ending at _closedCandles[endIndex].
    private decimal? Sma(int period, int endIndex)
    {
        if (endIndex < period - 1 || endIndex >= _closedCandles.Count) return null;
        decimal sum = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
            sum += _closedCandles[i].Close;
        return sum / period;
    }

    // Loads the WebView2 + historical seed only — connecting/subscribing the shared streamer is
    // MultiChartForm's job (once for all 3 panels), not each panel's.
    private async Task LoadHistoryAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();

            var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
            var navDone = new TaskCompletionSource();
            _webView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (args.IsSuccess) navDone.TrySetResult();
            };

            // Cache-busting query string — WebView2's Chromium engine can serve a cached copy of
            // chart.html across window instances within the same app process since it's always
            // the exact same file:// URL, even after the file on disk changed. Appending the
            // file's last-write time makes every rebuild/deploy get its own distinct URL, forcing
            // a fresh read instead of a stale cached one.
            var chartUri = new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}";
            _webView.CoreWebView2.Navigate(chartUri);
            await navDone.Task;

            // SMA 20/40/100/200 overlay — only on the 1h panel for now.
            if (_mode == ChartPanelMode.Hourly15)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configurarSMAs([20,40,100,200]);");

                // T-Line + vertical-arrow persistence (per symbol) — reload whatever was drawn in
                // a previous session so it reappears at the same point, and listen for new/
                // deleted/moved ones from now on so they get saved too.
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                var savedLines = TLineStore.Load(_symbol);
                if (savedLines.Count > 0)
                {
                    var linesJson = JsonSerializer.Serialize(savedLines.Select(l => new { t1 = l.T1, p1 = l.P1, t2 = l.T2, p2 = l.P2 }));
                    await _webView.CoreWebView2.ExecuteScriptAsync($"cargarTLines({linesJson});");
                }

                var savedArrows = VerticalArrowStore.Load(_symbol);
                if (savedArrows.Count > 0)
                {
                    var arrowsJson = JsonSerializer.Serialize(savedArrows.Select(a => new { time = a.Time, price = a.Price, up = a.Up }));
                    await _webView.CoreWebView2.ExecuteScriptAsync($"cargarFlechas({arrowsJson});");
                }
            }

            // Bollinger Bands (20, 2 std devs) — only on the 15m RTH panel for now.
            if (_mode == ChartPanelMode.Fifteen_RTH)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configurarBollinger(20, 2);");

                // Pre-market blue line: only if the chart is opened before 9:30 AM ET that day —
                // starts at the moment of opening and tracks live price until the market opens,
                // then freezes (see Streamer_OnNewCandle). Not persisted; a later re-open restarts
                // the whole thing from scratch.
                var nowUtc = DateTime.UtcNow;
                var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, EasternZone);
                if (nowEastern.TimeOfDay < new TimeSpan(9, 30, 0))
                {
                    var startTime = FakeUtcEpochSeconds(nowUtc);
                    await _webView.CoreWebView2.ExecuteScriptAsync($"iniciarPreMarketLine({startTime});");
                }
            }

            // Gray shading for overnight/weekend gaps — only on the 15m RTH+Overnight panel.
            if (_mode == ChartPanelMode.Fifteen_Full)
                await _webView.CoreWebView2.ExecuteScriptAsync("configurarOvernightBands();");

            // Default zoom on open: 1h panel shows the last 7 days, the two 15m panels show the
            // last 3 — full history is still loaded underneath for SMA/Bollinger, this only
            // limits the initial visible window (user can still scroll/zoom out manually).
            var visibleDays = _mode == ChartPanelMode.Hourly15 ? 7 : 3;
            await _webView.CoreWebView2.ExecuteScriptAsync($"configurarDiasVisibles({visibleDays});");

            // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day.
            // 1h panel shows the full 10 days; the two 15m panels show the last 3 days.
            var requestDays = _mode == ChartPanelMode.Hourly15 ? 10 : 3;
            var history = await _historyClient.GetHistoricalCandlesAsync(_symbol, requestDays);
            if (history.Count > 0)
            {
                var filtered = FilterSession(history, _rthOnly);
                _rawHistory = filtered; // cached so ToggleIntervalAsync can re-aggregate without re-fetching
                var aggregated = AggregateToInterval(filtered, _intervalMinutes, _rthOnly);

                // 1h panel: persist today's fetch to disk and merge with everything saved from
                // previous sessions, so SMA 100/200 can accumulate beyond Schwab's 10-day limit.
                if (_mode == ChartPanelMode.Hourly15 && aggregated.Count > 0)
                {
                    HourlyCandleStore.AppendIfMissing(_symbol, aggregated);
                    aggregated = HourlyCandleStore.Load(_symbol);
                }

                if (aggregated.Count > 0)
                {
                    await RunScriptAsync("cargarHistorial", aggregated);
                    // Seed the live aggregator with the last historical bucket so the first live
                    // tick extends it correctly instead of starting a spurious new one.
                    var last = aggregated[^1];
                    _liveAnchor      = BucketAnchor(new[] { last }, _rthOnly);
                    _liveBucketIndex = BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
                    _liveBucket      = last;

                    // Seed Cross-SMA monitoring's closed-candle history — everything fetched here
                    // is already closed (it's historical data); the live aggregator (above) owns
                    // the currently-forming candle separately.
                    if (_mode == ChartPanelMode.Hourly15)
                    {
                        _closedCandles.Clear();
                        _closedCandles.AddRange(aggregated);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_closing) return;
            MessageBox.Show($"Could not load the live chart for {_symbol} ({ModeLabel(_mode)}):\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // rthOnly keeps only 9:30 AM - 4:00 PM ET on each day present in the data (regular session);
    // otherwise keeps everything Schwab returned (regular + pre/after-hours). No longer
    // restricted to a single day — covers however many days were requested.
    private static List<CandleData> FilterSession(List<CandleData> candles, bool rthOnly)
    {
        if (!rthOnly) return candles;

        var rthStart = new TimeSpan(9, 30, 0);
        var rthEnd   = new TimeSpan(16, 0, 0);

        return candles
            .Where(c =>
            {
                var eastern = TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone);
                return eastern.TimeOfDay >= rthStart && eastern.TimeOfDay <= rthEnd;
            })
            .ToList();
    }

    // RTH buckets anchor at 9:30 AM ET (matching the regular session open); full-day buckets
    // anchor at midnight ET. Same anchor logic used for both historical batch aggregation and
    // live incremental aggregation, so bucket boundaries always agree.
    private static DateTime BucketAnchor(IEnumerable<CandleData> candles, bool rthOnly) =>
        candles
            .Select(c => TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone))
            .Min(t => rthOnly ? t.Date.AddHours(9).AddMinutes(30) : t.Date);

    private static int BucketIndex(DateTime utcTime, DateTime anchorEastern, int intervalMinutes)
    {
        var eastern   = TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternZone);
        var minutesIn = (eastern - anchorEastern).TotalMinutes;
        return (int)Math.Floor(minutesIn / intervalMinutes);
    }

    // Groups 1-minute candles into fixed-size buckets for the historical seed. Open = first
    // minute's open, Close = last minute's close, High/Low = extremes across the bucket.
    private static List<CandleData> AggregateToInterval(List<CandleData> minuteCandles, int intervalMinutes, bool rthOnly)
    {
        if (minuteCandles.Count == 0) return minuteCandles;

        var anchor = BucketAnchor(minuteCandles, rthOnly);

        return minuteCandles
            .GroupBy(c => BucketIndex(c.Time, anchor, intervalMinutes))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ordered = g.OrderBy(c => c.Time).ToList();
                return new CandleData
                {
                    Time  = ordered[0].Time,
                    Open  = ordered[0].Open,
                    Close = ordered[^1].Close,
                    High  = ordered.Max(c => c.High),
                    Low   = ordered.Min(c => c.Low)
                };
            })
            .ToList();
    }

    // Every 1-minute tick from the shared streamer lands here for all 3 panels. Each panel
    // decides independently whether the tick belongs to its current bucket (extend it) or starts
    // a new one (append) — this is what lets one WebSocket connection feed 3 different intervals.
    // Fires for every raw 1-minute tick this panel receives, regardless of session filtering —
    // used by MultiChartForm to show a live "time — price" readout above the 15m RTH+Overnight
    // panel. Eastern wall-clock time, same conversion used everywhere else in this class.
    public event Action<DateTime, decimal>? OnLiveTick;

    private void Streamer_OnNewCandle(string symbol, CandleData candle)
    {
        if (symbol != _symbol) return; // one shared connection carries all 4 tickers — ignore ticks for the others
        if (_closing || !IsHandleCreated) return;

        var eastern = TimeZoneInfo.ConvertTimeFromUtc(candle.Time, EasternZone);
        OnLiveTick?.Invoke(eastern, candle.Close);
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
        {
            // Pre-market tick on the 15m RTH panel — doesn't form a candle, but feeds the blue
            // pre-market line (if iniciarPreMarketLine was called when this panel opened). Once
            // 9:30 AM ET hits this branch stops firing for that reason, which is what freezes the
            // line in place with no extra "freeze" logic needed.
            if (_mode == ChartPanelMode.Fifteen_RTH && eastern.TimeOfDay < new TimeSpan(9, 30, 0))
            {
                var price = candle.Close;
                BeginInvoke(async () =>
                {
                    if (_webView.CoreWebView2 == null) return;
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        $"actualizarPreMarketLine({price.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
                });
            }
            return; // outside this panel's session — ignore the tick entirely
        }

        if (_liveBucket == null)
        {
            _liveAnchor      = eastern.Date.AddHours(_rthOnly ? 9 : 0).AddMinutes(_rthOnly ? 30 : 0);
            _liveBucketIndex = BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
            _liveBucket      = new CandleData { Time = candle.Time, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
        }
        else
        {
            var index = BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
            if (index != _liveBucketIndex)
            {
                // New bucket started — the previous one is now definitively closed (its last
                // update already reflects its final close). Feed Cross-SMA monitoring with it.
                if (_mode == ChartPanelMode.Hourly15)
                {
                    _closedCandles.Add(_liveBucket);
                    EvaluateCrossings(_liveBucket);
                }

                _liveBucketIndex = index;
                _liveBucket = new CandleData { Time = candle.Time, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
            }
            else
            {
                _liveBucket.High  = Math.Max(_liveBucket.High, candle.High);
                _liveBucket.Low   = Math.Min(_liveBucket.Low, candle.Low);
                _liveBucket.Close = candle.Close;
            }
        }

        var toSend = _liveBucket;
        BeginInvoke(async () => await RunScriptAsync("actualizarUltimaVela", toSend));
    }

    // Real-time last-price update (LEVEL_ONE_EQUITIES, much higher frequency than CHART_EQUITY's
    // 1-minute bars). Only ever adjusts the CURRENTLY-forming bucket's Close (and extends
    // High/Low if the tick exceeds them) — CHART_EQUITY still owns bucket boundaries and Open, so
    // this can't desync the two feeds, it just makes the live price shown track the true last
    // trade instead of waiting for the next full-minute bar.
    private void Streamer_OnLevelOneTick(string symbol, decimal price, DateTime utcTime)
    {
        if (symbol != _symbol) return;
        if (_closing || !IsHandleCreated) return;
        if (_liveBucket == null) return; // no bucket open yet — CHART_EQUITY seeds the first one

        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternZone);
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
            return; // outside this panel's session — ignore the tick entirely

        _liveBucket.High  = Math.Max(_liveBucket.High, price);
        _liveBucket.Low   = Math.Min(_liveBucket.Low, price);
        _liveBucket.Close = price;

        var toSend = _liveBucket;
        BeginInvoke(async () => await RunScriptAsync("actualizarUltimaVela", toSend));
    }

    private void Streamer_OnDisconnected(string message)
    {
        if (_closing || !IsHandleCreated) return;
        BeginInvoke(() => _header.Text = $"{_symbol} — {ModeLabel(_mode)} — {message}");
    }

    // Serializes the payload as JSON and calls the given JS function with it — used for both
    // cargarHistorial(velas[]) and actualizarUltimaVela(vela).
    private async Task RunScriptAsync(string jsFunction, object payload)
    {
        // With the shared streamer, ticks for this panel's symbol can start arriving before this
        // panel's own WebView2 has finished initializing (LoadHistoryAsync's
        // EnsureCoreWebView2Async/Navigate hasn't completed yet) — just drop those, the historical
        // seed load will catch the chart up once it's ready.
        if (_webView.CoreWebView2 == null) return;

        // CandleData's C# PascalCase properties need to map to Lightweight Charts' lowercase
        // fields — remap explicitly rather than relying on serializer naming policy tricks.
        await _webView.CoreWebView2.ExecuteScriptAsync($"{jsFunction}({ToChartJson(payload)});");
    }

    // Lightweight Charts renders the Unix timestamp we give it as literal UTC digits — it does
    // NOT convert to the browser's local timezone. So instead of sending the true UTC instant, we
    // convert to US Eastern wall-clock time first, then lie and mark THAT as UTC — the digits the
    // chart displays then read as New York time, regardless of what timezone the PC is set to.
    private static string ToChartJson(object payload)
    {
        static object Map(CandleData c)
        {
            return new
            {
                time  = FakeUtcEpochSeconds(c.Time),
                open  = c.Open,
                high  = c.High,
                low   = c.Low,
                close = c.Close
            };
        }

        return payload switch
        {
            CandleData single => JsonSerializer.Serialize(Map(single)),
            List<CandleData> many => JsonSerializer.Serialize(many.Select(Map)),
            _ => "null"
        };
    }

    // Same "ET wall-clock digits disguised as UTC" conversion ToChartJson uses for candles, split
    // out so the pre-market line's start time (not a candle) can use it too.
    private static long FakeUtcEpochSeconds(DateTime utcTime)
    {
        var easternWallClock = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
        var fakeUtcForDisplay = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Utc);
        return new DateTimeOffset(fakeUtcForDisplay).ToUnixTimeSeconds();
    }
}
