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

// A price's position relative to a Bollinger Bands(20,2) envelope — used by the premarket
// "Expuesto en 3 charts" check (see ChartPanel.GetBollingerDirection/GetDailyBollingerDirection
// and MultiChartForm's wiring of OnPreMarketPriceUpdated).
public enum BollingerDirection { None, Above, Below }

// One WebView2-hosted candlestick chart. Does NOT own a streaming connection — it's handed a
// SchwabStreamerClient for one-off REST history fetches (GetHistoricalCandlesAsync, no per-
// account limit on that) and a separate ICandleFeed for live ticks. In this app instance's own
// process the live feed might be that SAME SchwabStreamerClient (if this instance is the "hub"
// that owns the one Schwab streaming connection allowed per account) or a CandleHubClient
// relaying another instance's connection over localhost (if it isn't) — ChartPanel doesn't need
// to know which.
public class ChartPanel : Panel
{
    // Piso/Techo auto-analysis (1h panel only) — computed ONCE per running app instance (this
    // process = one ticker, so "once per instance" and "once per symbol" are the same thing
    // here), the first time Live Chart is opened and only if that happens before 9:30 AM ET.
    // Static so the decision survives closing and reopening the Live Chart window later the same
    // day (the WebView/ChartPanel itself gets fully disposed and recreated on each open) — see
    // EvaluatePisoTechoOnce, called from LoadHistoryAsync's Hourly15 branch.
    private static bool s_pisoTechoAnalyzed;
    // One independent result PER SMA (not per pair) — within the (20,40) pair, 20 and 40 can each
    // independently say "Piso", "Techo", or null. This matters when price opens BETWEEN the two:
    // e.g. bearish alignment (20 < 40) with 20 < price < 40 means 40 still hasn't been broken
    // (Techo) but 20 already has (nothing) — evaluating the pair as one unit would have missed
    // this and left both blank. See EvaluatePisoTechoPair.
    private static string? s_pisoTechoResult20;
    private static string? s_pisoTechoResult40;
    private static string? s_pisoTechoResult100;
    private static string? s_pisoTechoResult200;

    // Runs once, at the RTH market-open transition (see Streamer_OnNewCandle's !sameDay branch) —
    // if the actual opening price already broke a Piso/Techo SMA before the regular session even
    // started (gapped through it), that label no longer means anything and gets removed. Static
    // for the same "survives closing/reopening Live Chart" reason as s_pisoTechoAnalyzed.
    private static bool s_pisoTechoOpenValidated;

    // Auto-armed Cruce/Rebote watch, one entry PER PERIOD (not per pair) — when a pair comes back
    // Piso or Techo, BOTH its periods get armed independently (20 and 40 each watch their own
    // line separately, even though they're always the same direction within a pair — Piso/Techo
    // is decided per pair, never split). Runs independently, same precedent as
    // EvaluateDemandZoneRebounds.
    private sealed class PisoTechoWatch
    {
        public int Period;
        public bool WatchingUp; // true = Techo (expects reject down / cross up), false = Piso (expects bounce up / cross down)
        public bool Done;
    }
    private static readonly List<PisoTechoWatch> s_pisoTechoWatches = new();

    // "1er Rebote: 90%" label (1h panel only) — true once ANY closed candle's High has reached the
    // SMA20 Techo level since it was armed, regardless of whether that touch actually resolved into
    // a full Cruce/Rebote. Reset whenever the SMA20 Techo watch gets (re)armed. Static for the same
    // reason as the rest of this feature — survives closing/reopening the Live Chart same-session.
    private static bool s_sma20TechoTouched;

    // Blue premarket-price line + "Expuesto" text, keyed by "{symbol}_{mode}" — remembers the
    // frozen price/Bollinger-exposure it had right at market open so it can be redrawn exactly as
    // it was if the user closes and reopens the Live Chart mid-RTH-session (LoadHistoryAsync only
    // calls startPreMarketLine() itself when opened BEFORE 9:30, so without this a reopen after the
    // open would otherwise lose the line entirely). Static/in-memory only — same-session lifetime,
    // like s_sma20TechoTouched above; cleared implicitly by the Date check once a new day starts.
    private static readonly Dictionary<string, (DateOnly Date, decimal Price, BollingerDirection Exposed)> s_preMarketLineState = new();

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

    // Auto-drawn prev-day High/Low red H-Lines (see DrawPrevDayHiLoAsync) fire exactly once per
    // chart open — either synchronously at load (after 9:30, or Fifteen_Full before it) or on the
    // first pre-market tick (Hourly15/Fifteen_RTH before 9:30). This guards against firing twice.
    private bool _drewPrevDayHiLo;

    // Guards against double-subscribing WebMessageReceived when LoadHistoryAsync re-runs after a
    // WebView2 renderer crash (see ProcessFailed handling below) — without this, a crash-recovery
    // reload would leave every drawn T-Line/arrow/rect etc. double-processed (appended to its store
    // twice, moved twice, etc.) for the rest of the session.
    private bool _webMessageHandlerAttached;

    // WebView2's own crash-recovery reload is blocked by Chromium's same-origin policy for file://
    // URLs whenever the cache-busting query string changed since the page loaded (confirmed live:
    // "Unsafe attempt to load URL chart.html?v=X from frame with URL chart.html?v=Y — 'file:' URLs
    // are treated as unique security origins") — that leaves the panel permanently blank after a
    // renderer crash, since nothing else ever re-Navigates it. ProcessFailed (subscribed once,
    // guarded by _processFailedHandlerAttached) detects that and re-runs LoadHistoryAsync itself —
    // an explicit host-initiated Navigate() isn't subject to that frame-origin check.
    private bool _processFailedHandlerAttached;
    private bool _crashReloadInProgress;

    // All-Time High reference line (all 3 panels) — see AllTimeHighStore. Null until loaded (or if
    // no file exists yet for this symbol). _athTodaysHigh only tracked on the 1h panel (avoids
    // 3 redundant file writes at the close) — running max of every live price seen today, compared
    // against _athValue at 16:00 ET to decide whether to persist a new one (EvaluateAllTimeHighAtClose).
    private decimal? _athValue;
    private decimal? _athTodaysHigh;
    private bool _athEvaluatedAtClose;

    // Fires (newValue) once the 1h panel persists a new All-Time High at the RTH close —
    // MultiChartForm mirrors it onto the other 2 panels (see MarkAllTimeHighAsync).
    public event Action<decimal>? OnAllTimeHighUpdatedEvent;

    // Temporary diagnostic — fires every time DrawPrevDayHiLoAsync actually runs its computation,
    // regardless of whether it ends up drawing anything, so a "why isn't this panel showing the
    // line" report can be answered from crossLog instead of guessing.
    public event Action<string>? OnPrevDayHiLoDebugEvent;

    // Temporary diagnostic — best-effort, never throws into a live-tick handler. Used to chase the
    // "last 15m RTH candle of yesterday visually stretches into today's open" report; safe to
    // remove once that's confirmed fixed.
    private static void DebugLog(string message)
    {
        try
        {
            Directory.CreateDirectory(@"C:\OptionsData\EventLog");
            File.AppendAllText(@"C:\OptionsData\EventLog\dayreset_debug.log",
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* best-effort diagnostic logging */ }
    }

    // The bucket currently being built from live 1-min ticks, and which bucket index it belongs
    // to (so we know when a new tick starts a new bucket vs. extends the current one).
    private CandleData? _liveBucket;
    private long? _liveBucketIndex;
    private DateTime _liveAnchor;

    // Closed candles (Hourly15/Fifteen_RTH) kept for computing SMA/Bollinger ourselves in C# (same
    // simple-average formula as the JS overlay) — used by the Piso/Techo watch system, T-Line
    // signal, and the premarket Bollinger-exposure check.
    private readonly List<CandleData> _closedCandles = new();

    // ---- Demand/Supply Zone rebote (15m RTH+Overnight panel only) ----
    // Every DZ/SZ line drawn (toggleDzSz — see CoreWebView2_WebMessageReceived's "dzsz" case)
    // arrives one at a time; every 2 form a pair. Geometry decides which kind of zone it is (per
    // the user's own convention — 1st click is always the green/Proximal line, 2nd is always the
    // red/Distal line, but which one ends up numerically higher tells them apart):
    //   Proximal ABOVE Distal -> Demand Zone, drawn BELOW price (bounce UP expected).
    //   Proximal BELOW Distal -> Supply Zone, drawn ABOVE price (bounce DOWN expected).
    // Cleared by ClearDrawingsAsync.
    private readonly List<decimal> _dzSzPendingPrices = new(); // holds an odd single line waiting for its pair
    private readonly List<DemandZoneState> _demandZones = new();
    private readonly List<SupplyZoneState> _supplyZones = new();

    private sealed class DemandZoneState
    {
        public decimal Proximal; // green line — upper boundary (closer to price, zone is below it)
        public decimal Distal;   // red line — lower boundary
        public bool Entered;
        public bool Done; // Confirmed (rebote fired) or Broken (Distal breached) — stop evaluating
    }

    private sealed class SupplyZoneState
    {
        public decimal Proximal; // green line — lower boundary (closer to price, zone is above it)
        public decimal Distal;   // red line — upper boundary
        public bool Entered;
        public bool Done;
    }

    // T-Line + SMA20 breakout signal (Hourly15 panel only, see EvaluateTLineSignal): only one
    // T-Line is ever allowed to exist for a symbol at a time — enforced in
    // CoreWebView2_WebMessageReceived — so there's no ambiguity about which line to evaluate
    // against. Fires once per T-Line, then stays silent until that line is deleted and a new one
    // drawn (_tLineSignalFired resets in both those cases).
    private bool _tLineSignalFired;

    // Fires with a human-readable caption when the T-Line+SMA20 breakout signal triggers — the
    // caller (MultiChartForm) both logs it and pushes the combined 3-chart Telegram snapshot.
    public event Action<string>? OnTLineSignalEvent;

    // Fires once, right after history loads (Hourly15 only — see EvaluateDailyBounce), if
    // yesterday's already-closed daily candle bounced off the daily SMA20. Purely informational —
    // the caller just logs it and shows a hint on the chart; no Telegram push, no automatic action.
    public event Action<string>? OnDailyBounceEvent;

    // Fires with a human-readable caption when a Demand Zone rebote is confirmed (15m
    // RTH+Overnight panel only — see EvaluateDemandZoneRebounds). Pushes its own screenshot to
    // Telegram the same self-contained way Cross-SMA does (SendChartToTelegramAsync below).
    public event Action<string>? OnDemandZoneReboundEvent;

    // Symmetric counterpart — Rebote en Zona de Supply (see EvaluateSupplyZoneRebounds).
    public event Action<string>? OnSupplyZoneReboundEvent;

    // Armed by EvaluateDemandZoneRebounds/EvaluateSupplyZoneRebounds the moment a rebote confirms
    // — from then on, OnAutoZonePushTickEvent fires on every closed 15m candle (below) until
    // StopAutoZonePush() disarms it (or a new rebote re-arms it later). MultiChartForm listens for
    // the tick event and pushes the combined 3-chart snapshot to Telegram each time.
    private bool _autoZonePushArmed;
    public event Action<CandleData>? OnAutoZonePushTickEvent;
    public void StopAutoZonePush() => _autoZonePushArmed = false;

    // Fires (evento, pisoTecho, caption) every time a Piso/Techo watch resolves — 1h panel only.
    // MultiChartForm uses evento/pisoTecho to arm the 15m RTH panel's "Abriendo la Volatilidad"
    // watch (see EvaluateVolatilityOpening on that panel), and mirrors caption into crossLog —
    // only the pre-market Piso/Techo LABELS are chart-only; the real-time Cruce/Rebote resolution
    // itself does get logged everywhere else (crossLog, Telegram, EventLogStore), same as every
    // other signal.
    public event Action<string, string, string>? OnPisoTechoResolvedEvent;

    // Fires (period, price) for each armed SMA — first pre-market (EvaluatePisoTechoOnce), then
    // again on every closed 1h candle for the rest of the session (see the sameDay branch below),
    // so the price always reflects that SMA's CURRENT value, not a frozen pre-market snapshot.
    // MultiChartForm forwards this to the 15m RTH/RTH+Overnight panels (MarkPisoTechoRefLineAsync)
    // to draw/move a dashed reference line at that price, same color as the SMA, so the level is
    // visible there too without needing the 1h panel open. Fires (period) alone when that level
    // gets invalidated by the market-open gap (ValidatePisoTechoAgainstOpen/
    // InvalidateIfBrokenByOpen below) — forwarded the same way to remove the matching reference line.
    public event Action<int, decimal>? OnPisoTechoLevelReadyEvent;
    public event Action<int>? OnPisoTechoLevelRemovedEvent;

    // Fires (detail) whenever SendChartToTelegramAsync fails — previously every call site was
    // fire-and-forget with the failure silently discarded (bad/missing credentials, network error,
    // Telegram API error, or the chart WebView2 not being ready yet), leaving no trace anywhere
    // that a push never went out. MultiChartForm mirrors this into crossLog so it's diagnosable.
    public event Action<string>? OnTelegramPushFailedEvent;

    // Fires (price) when a Stk line gets deleted (selected + Delete) on THIS panel — MultiChartForm
    // uses this to remove the matching Stk line on the other 2 panels too, since markStrike draws
    // the same line on all 3 at once. See CoreWebView2_WebMessageReceived's "strike_delete" case.
    public event Action<decimal>? OnStrikeDeletedEvent;

    // Fires (price) when an H-Line gets deleted (selected + Delete) on THIS panel — covers both
    // manually-drawn H-Lines and the auto-drawn prev-day High/Low (same price drawn independently
    // on all 3 panels by markPrevDayHiLo). MultiChartForm uses this to remove the matching line on
    // the other 2 panels too. See CoreWebView2_WebMessageReceived's "hline_delete" case.
    public event Action<decimal>? OnHLineDeletedEvent;

    // Fires (time, price) when a NEW H-Line gets drawn (2 clicks) on THIS panel — MultiChartForm
    // uses this to mirror it onto the other 2 panels. See CoreWebView2_WebMessageReceived's
    // "hline_add" case and addMirroredHLine in chart.html.
    public event Action<long, decimal>? OnHLineDrawnEvent;

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

        // "Potencial CT al Alza/Baja" and daily-bounce hints — 1h panel only, rendered as a green
        // overlay INSIDE the chart itself (chart.html's #hints div, via setTLineHint/
        // setDailyBounceHint) rather than a WinForms Label docked below the WebView. A docked
        // Label reserves its Height even with empty Text, which made the 1h panel's chart area
        // shorter than the other 2 panels (no such labels) whenever no hint was active — an
        // overlay costs no layout space either way, so all 3 panels stay the same height.

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
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleDzSz();");
        return result == "true";
    }

    // Toggles Rect drawing mode on/off. While on, every pair of clicks draws a new sky-blue
    // rectangle between them (opposite corners, no value labels). Same toggle pattern as DZ/SZ.
    public async Task<bool> ToggleRectModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleRect();");
        return result == "true";
    }

    // Toggles the 1h panel's gray Rect tool on/off — same 2-click draw as Rect, but filled gray
    // (marking sideways/consolidation ranges) and each rectangle can be selected by clicking its
    // border and removed with the Delete key, independent of whether the tool is armed.
    public async Task<bool> ToggleRectGrisModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleGrayRect();");
        return result == "true";
    }

    // Toggles T-Line drawing mode on/off. While on, every pair of clicks draws a new orange line
    // segment between them (not extended to the chart edges). Same toggle pattern as Rect.
    public async Task<bool> ToggleTLineModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleTLine();");
        return result == "true";
    }

    // Fired when a T-Line gets drawn/deleted on THIS panel (any of the 3, not just the 1h one —
    // see CoreWebView2_WebMessageReceived's "tline"/"tline_delete" case) — MultiChartForm mirrors
    // it onto the other 2 panels, same pattern as OnStrikeDeletedEvent/OnHLineDeletedEvent.
    public event Action<long, decimal, long, decimal>? OnTLineDrawnEvent;
    public event Action<long, decimal, long, decimal>? OnTLineRemovedEvent;

    // Draws/removes a T-Line MIRRORED from another panel — additive (addMirroredTLine), doesn't go
    // through this panel's own "only 1 T-Line at a time" limit or TLineStore at all, since the
    // line already exists for real on the originating panel.
    public async Task AddMirroredTLineAsync(long t1, decimal p1, long t2, decimal p2)
    {
        if (_webView.CoreWebView2 == null) return;
        var p1Str = p1.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var p2Str = p2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"addMirroredTLine({t1}, {p1Str}, {t2}, {p2Str});");
    }

    public async Task RemoveMirroredTLineAsync(long t1, decimal p1, long t2, decimal p2)
    {
        if (_webView.CoreWebView2 == null) return;
        var p1Str = p1.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var p2Str = p2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"removeMirroredTLine({t1}, {p1Str}, {t2}, {p2Str});");
    }

    // Toggles H-Line drawing mode on/off. While on, every click draws a new red horizontal line
    // from the click point to the right edge of the chart. Same toggle pattern as DZ/SZ.
    public async Task<bool> ToggleHLineModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleHLine();");
        return result == "true";
    }

    // Toggles Arrow drawing mode on/off. While on, every pair of clicks draws a line + arrowhead
    // between them — red if the 1st click is above (higher price than) the 2nd, green otherwise.
    // Same toggle pattern as Rect/T-Line.
    public async Task<bool> ToggleArrowModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleArrow();");
        return result == "true";
    }

    // Programmatic (not click-driven) red "Expired!!!" marker at the most recent candle — used
    // by the 4pm ET expiration auto-close, not exposed via any UI toggle.
    public async Task MarkExpiredAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("markExpired();");
    }

    // Toggles the dashed vertical day-divider lines on/off — only meaningful on the 1h panel
    // (Hourly15), separates the last 5 days' worth of hourly candles with a line at the start of
    // each of the last 4 (today's candles sit to the right of the most recent one, unbounded).
    public async Task<bool> ToggleDayDividersAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleDayDividers();");
        return result == "true";
    }

    // Full-width green line + "Stk=xxx" label at the given price — fired when a trade (demo or
    // real) opens, on all 3 panels. Accumulates across trades, never auto-removed.
    public async Task MarkStrikeAsync(decimal strike)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markStrike({priceStr});");
    }

    // Removes a Stk line at the given price — called on the 2 SIBLING panels when OnStrikeDeletedEvent
    // fires from wherever the user actually clicked + pressed Delete (see MultiChartForm).
    public async Task RemoveStrikeLineAsync(decimal strike)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"removeStrikeLine({priceStr});");
    }

    // Removes an H-Line at the given price — called on the 2 SIBLING panels when
    // OnHLineDeletedEvent fires from wherever the user actually clicked + pressed Delete.
    public async Task RemoveHLineAsync(decimal price)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"removeHLine({priceStr});");
    }

    // Adds an H-Line to THIS panel without arming click mode — called on the 2 SIBLING panels when
    // OnHLineDrawnEvent fires from wherever the user actually drew it. Same idea as
    // AddMirroredTLineAsync.
    public async Task AddMirroredHLineAsync(long time, decimal price)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"addMirroredHLine({time}, {priceStr});");
    }

    // Draws/updates this panel's All-Time High reference line — called on chart open (loaded from
    // AllTimeHighStore) and again on the other 2 panels when the 1h panel persists a new one at the
    // close (see OnAllTimeHighUpdatedEvent).
    public async Task MarkAllTimeHighAsync(decimal price)
    {
        _athValue = price;
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markAllTimeHigh({priceStr});");
    }

    // Shows/hides the ATH reference line — a toolbar checkbox, per explicit request, same
    // show/hide-only convention as SetBollingerEdgeMarkersVisibleAsync.
    public async Task SetAllTimeHighVisibleAsync(bool show)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"setAllTimeHighVisible({(show ? "true" : "false")});");
    }

    // Shows/hides the white Bollinger-band edge markers (panel 15m RTH only) — a toolbar checkbox,
    // per explicit request. The underlying calculation keeps running either way (see
    // enableBollingerEdgeMarkers/recalculateBollinger in chart.html); this only toggles the draw.
    public async Task SetBollingerEdgeMarkersVisibleAsync(bool show)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"setBollingerEdgeMarkersVisible({(show ? "true" : "false")});");
    }

    // White line at the underlying spot price the moment a trade is opened or closed — panel 3
    // (RTH+Overnight) only, per explicit request extending the Simulator's identical marker to the
    // live app. Same markEntrySpot JS function the Simulator already uses (anchors to whichever
    // candle is currently last in the series, spans 3 bar-widths). Accumulates, one segment per
    // call, never auto-removed.
    public async Task MarkEntrySpotAsync(decimal price)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markEntrySpot({priceStr});");
    }

    // Re-evaluated on every live tick (all 3 panels) — purely visual, flips the ATH line green
    // while the live price is currently trading above it, gold otherwise. Independent of whether
    // today's high actually gets persisted as a new ATH (that only happens once, at the close).
    private async Task MarkAllTimeHighBrokenAsync(bool show)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"updateAllTimeHighBroken({(show ? "true" : "false")});");
    }

    // "ΔS=value" label at trade close — anchored at the trade's strike (same price as its green
    // "Stk=xxx" line), drawn just below it. See markDeltaS in chart.html for the exact rationale.
    public async Task MarkDeltaSAsync(decimal entrySpot, decimal closeSpot, decimal strike)
    {
        if (_webView.CoreWebView2 == null) return;
        var entryStr  = entrySpot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var closeStr  = closeSpot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var strikeStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markDeltaS({entryStr}, {closeStr}, {strikeStr});");
    }

    // Dashed Piso/Techo reference line (15m RTH / RTH+Overnight panels) — called by MultiChartForm
    // when the 1h panel's OnPisoTechoLevelReadyEvent/OnPisoTechoLevelRemovedEvent fire. See
    // markPisoTechoRefLine/removePisoTechoRefLine in chart.html for the rendering.
    public async Task MarkPisoTechoRefLineAsync(int period, decimal price, long sessionStartFakeEpoch, long sessionEndFakeEpoch)
    {
        // Temporary diagnostic — chasing a report where this silently never reaches chart.html on
        // ONE panel (confirmed via pisoTechoRefLineAttached staying false in DevTools) while the
        // sibling panel, called from the exact same C# call site, works fine. Safe to remove once
        // the cause is confirmed and fixed.
        if (_webView.CoreWebView2 == null)
        {
            DebugLog($"MarkPisoTechoRefLineAsync SKIPPED (CoreWebView2 null): symbol={_symbol} mode={_mode} period={period}");
            return;
        }
        try
        {
            var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _webView.CoreWebView2.ExecuteScriptAsync($"markPisoTechoRefLine({period}, {priceStr}, {sessionStartFakeEpoch}, {sessionEndFakeEpoch});");
            DebugLog($"MarkPisoTechoRefLineAsync OK: symbol={_symbol} mode={_mode} period={period} price={priceStr}");
        }
        catch (Exception ex)
        {
            DebugLog($"MarkPisoTechoRefLineAsync THREW: symbol={_symbol} mode={_mode} period={period} ex={ex}");
        }
    }

    public async Task RemovePisoTechoRefLineAsync(int period)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"removePisoTechoRefLine({period});");
    }

    // Toggles the 1h panel's vertical arrow tools on/off. While on, every click places a
    // fixed-length vertical arrow with its tip at the clicked point — green points up, red points
    // down. Selectable by clicking the shaft and removable with Delete, same as the gray Rect
    // tool.
    public async Task<bool> ToggleFlechaVerdeModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleGreenArrow();");
        return result == "true";
    }

    public async Task<bool> ToggleFlechaRojaModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleRedArrow();");
        return result == "true";
    }

    // Clears every DZ/SZ pair, rectangle, T-Line, H-Line, Arrow and Piso/Techo label drawn on
    // this panel, and turns all drawing modes off. Also wipes the persisted T-Line/vertical-arrow
    // files for this symbol (1h panel only) — a real "clear" should clear what's saved too.
    public async Task ClearDrawingsAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("clearDrawings();");
        if (_mode == ChartPanelMode.Hourly15)
        {
            TLineStore.Clear(_symbol);
            VerticalArrowStore.Clear(_symbol);
            RectGrisStore.Clear(_symbol);
            _tLineSignalFired = false;
            _ = _webView.CoreWebView2?.ExecuteScriptAsync("setTLineHint('');");
        }
        if (_mode == ChartPanelMode.Fifteen_Full)
        {
            _dzSzPendingPrices.Clear();
            _demandZones.Clear();
            _supplyZones.Clear();
        }
    }

    // Sets the "Potencial CT al Alza/Baja" hint right after a T-Line finishes drawing — direction
    // comes from how the line itself was drawn (technical-analysis convention: a descending line
    // acts as resistance, so breaking it is a bullish signal; an ascending line acts as support,
    // so breaking it is bearish):
    //   drawn top-to-bottom (1st click's price ABOVE the 2nd's) → descending → "al Alza"
    //   drawn bottom-to-top (1st click's price BELOW the 2nd's) → ascending  → "a la Baja"
    private void UpdateTLineHint(decimal p1, decimal p2)
    {
        var text = p1 > p2 ? "Potencial CT al Alza" : "Potencial CT a la Baja";
        _ = _webView.CoreWebView2?.ExecuteScriptAsync($"setTLineHint({JsonSerializer.Serialize(text)});");
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
                    if (type == "tline")
                    {
                        // Only 1 T-Line allowed at a time (the breakout signal below needs an
                        // unambiguous line to evaluate against) — reject a 2nd one, undo it on
                        // the chart, and tell the user why.
                        if (TLineStore.Load(_symbol).Count > 0)
                        {
                            _ = _webView.CoreWebView2.ExecuteScriptAsync("removeLastTLine();");
                            MessageBox.Show(
                                "Ya existe una T-Line dibujada para este símbolo. Borra la actual (selecciónala y presiona Delete) antes de dibujar una nueva.",
                                "T-Line ya existe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            break;
                        }
                        TLineStore.Append(_symbol, t1, p1, t2, p2);
                        _tLineSignalFired = false;
                        UpdateTLineHint(p1, p2);
                        OnTLineDrawnEvent?.Invoke(t1, p1, t2, p2);
                    }
                    else
                    {
                        TLineStore.Remove(_symbol, t1, p1, t2, p2);
                        _tLineSignalFired = false;
                        _ = _webView.CoreWebView2?.ExecuteScriptAsync("setTLineHint('');");
                        OnTLineRemovedEvent?.Invoke(t1, p1, t2, p2);
                    }
                    break;
                }
                case "rect_add":
                case "rect_delete":
                {
                    var rt1 = root.GetProperty("t1").GetInt64();
                    var rp1 = root.GetProperty("p1").GetDecimal();
                    var rt2 = root.GetProperty("t2").GetInt64();
                    var rp2 = root.GetProperty("p2").GetDecimal();
                    if (type == "rect_add")
                        RectGrisStore.Append(_symbol, rt1, rp1, rt2, rp2);
                    else
                        RectGrisStore.Remove(_symbol, rt1, rp1, rt2, rp2);
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
                case "dzsz":
                {
                    var dzPrice = root.GetProperty("price").GetDecimal();
                    _dzSzPendingPrices.Add(dzPrice);
                    if (_dzSzPendingPrices.Count == 2)
                    {
                        var (demandPrice, supplyPrice) = (_dzSzPendingPrices[0], _dzSzPendingPrices[1]);
                        _dzSzPendingPrices.Clear();
                        if (demandPrice > supplyPrice) // 1st line (green) above 2nd (red) -> Demand Zone
                            _demandZones.Add(new DemandZoneState { Proximal = demandPrice, Distal = supplyPrice });
                        else if (demandPrice < supplyPrice) // 1st line (green) below 2nd (red) -> Supply Zone
                            _supplyZones.Add(new SupplyZoneState { Proximal = demandPrice, Distal = supplyPrice });
                    }
                    break;
                }
                case "strike_delete":
                {
                    // Deleted on THIS panel already (chart.html removes it locally before posting
                    // this) — fire the event so MultiChartForm can remove the matching line from
                    // the other 2 panels too (markStrike draws the same Stk line on all 3 at once).
                    var strikePrice = root.GetProperty("price").GetDecimal();
                    OnStrikeDeletedEvent?.Invoke(strikePrice);
                    break;
                }
                case "hline_delete":
                {
                    // Deleted on THIS panel already — fire the event so MultiChartForm can remove
                    // the matching line (by price) from the other 2 panels too.
                    var hLinePrice = root.GetProperty("price").GetDecimal();
                    OnHLineDeletedEvent?.Invoke(hLinePrice);
                    break;
                }
                case "hline_add":
                {
                    // Drawn on THIS panel already — fire the event so MultiChartForm can mirror it
                    // onto the other 2 panels (see AddMirroredHLineAsync).
                    var hAddTime  = root.GetProperty("time").GetInt64();
                    var hAddPrice = root.GetProperty("price").GetDecimal();
                    OnHLineDrawnEvent?.Invoke(hAddTime, hAddPrice);
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
            var aggregated = CandleAggregation.AggregateToInterval(_rawHistory, _intervalMinutes, _rthOnly);
            if (aggregated.Count > 0)
            {
                await RunScriptAsync("loadHistory", aggregated);
                var last = aggregated[^1];
                _liveAnchor      = CandleAggregation.BucketAnchor(new[] { last }, _rthOnly);
                _liveBucketIndex = CandleAggregation.BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
                _liveBucket      = last;
            }
        }

        return _intervalMinutes == 5;
    }

    // Captures this panel's chart as a PNG (via WebView2's native preview capture — pixel-exact,
    // doesn't depend on the window being visible/on top, unlike a screen-coordinate capture) and
    // pushes it to the configured Telegram channel.
    private async Task<(bool Ok, string Detail)> SendChartToTelegramAsync(string caption)
    {
        if (_webView.CoreWebView2 == null)
        {
            OnTelegramPushFailedEvent?.Invoke("Chart not loaded yet.");
            return (false, "Chart not loaded yet.");
        }

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
            var (ok, detail, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, path, $"{_symbol} — {caption}");
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, _symbol, "CrossSMA", DateTime.Now));
            if (ok)
                EventLogMarkdownWriter.AppendEvent(_symbol, caption, path);
            else
                OnTelegramPushFailedEvent?.Invoke(detail);
            return (ok, detail);
        }
        catch (Exception ex)
        {
            OnTelegramPushFailedEvent?.Invoke(ex.Message);
            return (false, ex.Message);
        }
    }

    // How much closer than the bounce-back move price has to get to the SMA to still count as a
    // "no-touch" bounce (case 2 below) — 30%: distance-to-SMA must be under 30% of the rejection
    // move for it to count as "went looking for the SMA and got rejected near it".
    private const decimal BounceProximityRatio = 0.30m;

    // Daily-candle bounce off the daily SMA20 — evaluated once per app run, right after the 1h
    // panel's history loads (only if this window is open at all; if it's closed, this never
    // runs). Checks the last already-CLOSED daily bar (yesterday — today's bar, if present in
    // `hourly`, is still forming and is excluded) against the daily SMA20, using the exact same
    // case-1/case-2 bounce formula used elsewhere (BounceProximityRatio), just on daily bars
    // instead of 1h ones, and with no Cruce detection at all — only Rebote.
    private void EvaluateDailyBounce(List<CandleData> hourly)
    {
        var daily = CandleAggregation.AggregateToDaily(hourly);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));
        daily = daily.Where(d => d.Date < today).ToList(); // drop today's still-forming bar, if any

        const int period = TLineSmaPeriod; // 20 — reused, same "SMA20" concept as T-Line signal
        if (daily.Count < period + 1) return; // need the bounce day itself + period prior closes

        var bars = daily.Select(d => d.Candle).ToList();
        var idx = bars.Count - 1; // yesterday — the last closed daily bar
        var justClosed = bars[idx];

        decimal sum = 0;
        for (int i = idx - period + 1; i <= idx; i++) sum += bars[i].Close;
        var sma20 = sum / period;

        var isGreen = justClosed.Close > justClosed.Open;
        var isRed   = justClosed.Close < justClosed.Open;

        // Approaching from below (Open < SMA20), rejected back down — case 1 (wick crossed but
        // close rejected) or case 2 (wick fell short, but came within 30% of the rejection move).
        var bouncedDown = justClosed.Open < sma20 && isRed &&
            (justClosed.High > sma20
                ? justClosed.Close < sma20
                : (sma20 - justClosed.High) < BounceProximityRatio * (justClosed.High - justClosed.Close));

        // Mirrored: approaching from above (Open > SMA20), rejected back up.
        var bouncedUp = justClosed.Open > sma20 && isGreen &&
            (justClosed.Low < sma20
                ? justClosed.Close > sma20
                : (justClosed.Low - sma20) < BounceProximityRatio * (justClosed.Close - justClosed.Low));

        if (!bouncedDown && !bouncedUp) return;

        var direction = bouncedUp ? "al alza" : "a la baja";
        var description = $"Rebote {direction} en Diario";
        OnDailyBounceEvent?.Invoke(description);
        var hintText = $"Análisis Diario: {description}";
        _ = _webView.CoreWebView2?.ExecuteScriptAsync($"setDailyBounceHint({JsonSerializer.Serialize(hintText)});");

        var eventDirection = bouncedUp ? "Alza" : "Baja";
        EventLogStore.Append(_symbol, "Diario", "DailyBounce", eventDirection, description, justClosed.Close, $"SMA20={sma20:F2}");
    }

    private const int TLineSmaPeriod = 20;

    // T-Line + SMA20 breakout: fires once (per T-Line — see _tLineSignalFired) when a just-closed
    // 1h candle crosses BOTH the T-Line and SMA20 in the same direction and closes past both —
    // either direction counts, mirrored:
    //   Al alza:  opened BELOW the T-Line, High got above BOTH T-Line and SMA20 during the
    //             candle (approximated with High since only OHLC is available), closed above both.
    //   A la baja: opened ABOVE the T-Line, Low got below BOTH during the candle, closed below both.
    // Automatic — runs for as long as exactly one T-Line is drawn, no arm/disarm toggle (unlike
    // Cross-SMA).
    private void EvaluateTLineSignal(CandleData justClosed)
    {
        if (_tLineSignalFired) return;

        var lines = TLineStore.Load(_symbol);
        if (lines.Count == 0) return; // enforced to be 0 or 1, never more

        var (t1, p1, t2, p2) = lines[0];
        var candleTimeSec = new DateTimeOffset(DateTime.SpecifyKind(justClosed.Time, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var tLineValue = TLineValueAt(t1, p1, t2, p2, candleTimeSec);

        if (_closedCandles.Count < TLineSmaPeriod) return; // not enough history for SMA20 yet
        var sma20 = Sma(TLineSmaPeriod, _closedCandles.Count - 1);
        if (sma20 == null) return;

        var upBreakout = justClosed.Open < tLineValue
            && justClosed.High > tLineValue && justClosed.High > sma20.Value
            && justClosed.Close > tLineValue && justClosed.Close > sma20.Value;

        var downBreakout = justClosed.Open > tLineValue
            && justClosed.Low < tLineValue && justClosed.Low < sma20.Value
            && justClosed.Close < tLineValue && justClosed.Close < sma20.Value;

        if (!upBreakout && !downBreakout) return;

        _tLineSignalFired = true;
        var direction = upBreakout ? "al alza" : "a la baja";
        var caption = $"CT {direction} en Hora — cierre {justClosed.Close:F2} (T-Line {tLineValue:F2}, SMA{TLineSmaPeriod} {sma20.Value:F2})";
        OnTLineSignalEvent?.Invoke(caption);

        var eventDirection = upBreakout ? "Alza" : "Baja";
        EventLogStore.Append(_symbol, "Hora", "TLineBreakout", eventDirection, caption, justClosed.Close,
            $"TLine={tLineValue:F2};SMA{TLineSmaPeriod}={sma20.Value:F2}");
    }

    // Demand Zone rebote (15m RTH+Overnight panel only): evaluated against every tracked demand
    // zone (see the "dzsz" case in CoreWebView2_WebMessageReceived) on every just-closed 15m
    // candle, independent of whichever other zones are also being tracked.
    //   Entrada: the candle's Low reaches the zone (<= Proximal) — marks it Entered. Same
    //     case-1/case-2 proximity idea used elsewhere (BounceProximityRatio): a candle whose Low
    //     falls SHORT of Proximal, but within BounceProximityRatio of the rejection move's size
    //     (Close - Low), still counts as touching it — "got close enough, rejected before
    //     actually reaching the line". Fires as an immediate confirmed rebote (Close > Proximal
    //     is guaranteed there, and Distal was never at risk).
    //   Rota (invalidated forever): the candle's Low breaches the Distal line (< Distal) at any
    //     point while/after entering — no rebote can fire for this zone again.
    //   Rebote confirmado (fires once): while Entered and not yet Broken, the candle's CLOSE ends
    //     up back outside (above) the Proximal line. Can fire on the very same candle that enters,
    //     if that candle's wick dips into the zone but still closes back above Proximal.
    private void EvaluateDemandZoneRebounds(CandleData justClosed)
    {
        foreach (var zone in _demandZones)
        {
            if (zone.Done) continue;

            if (!zone.Entered)
            {
                var touchedOrClose = justClosed.Low <= zone.Proximal ||
                    (justClosed.Low - zone.Proximal) < BounceProximityRatio * (justClosed.Close - justClosed.Low);
                if (!touchedOrClose) continue; // hasn't reached (or come close to) the zone yet
                zone.Entered = true;
            }

            if (justClosed.Low < zone.Distal)
            {
                zone.Done = true; // broken — no rebote possible for this zone anymore
                continue;
            }

            if (justClosed.Close > zone.Proximal)
            {
                zone.Done = true;
                _autoZonePushArmed = true; // start auto-pushing the combined 3-chart image on every closed candle from here
                var caption = $"Rebote en Zona de Demanda — cierre {justClosed.Close:F2} (Proximal {zone.Proximal:F2}, Distal {zone.Distal:F2})";
                OnDemandZoneReboundEvent?.Invoke(caption);
                _ = SendChartToTelegramAsync(caption);
                EventLogStore.Append(_symbol, "15Min", "DemandZoneRebound", "Alza", caption, justClosed.Close,
                    $"Proximal={zone.Proximal:F2};Distal={zone.Distal:F2}");
            }
        }
    }

    // Supply Zone rebote — exact mirror of EvaluateDemandZoneRebounds, flipped: the zone sits
    // ABOVE price (Proximal below Distal), approached from below by the candle's High instead of
    // Low, broken if High breaches Distal, and confirmed once Close ends up back BELOW Proximal
    // (bearish rejection instead of bullish).
    private void EvaluateSupplyZoneRebounds(CandleData justClosed)
    {
        foreach (var zone in _supplyZones)
        {
            if (zone.Done) continue;

            if (!zone.Entered)
            {
                var touchedOrClose = justClosed.High >= zone.Proximal ||
                    (zone.Proximal - justClosed.High) < BounceProximityRatio * (justClosed.High - justClosed.Close);
                if (!touchedOrClose) continue;
                zone.Entered = true;
            }

            if (justClosed.High > zone.Distal)
            {
                zone.Done = true; // broken
                continue;
            }

            if (justClosed.Close < zone.Proximal)
            {
                zone.Done = true;
                _autoZonePushArmed = true; // start auto-pushing the combined 3-chart image on every closed candle from here
                var caption = $"Rebote en Zona de Supply — cierre {justClosed.Close:F2} (Proximal {zone.Proximal:F2}, Distal {zone.Distal:F2})";
                OnSupplyZoneReboundEvent?.Invoke(caption);
                _ = SendChartToTelegramAsync(caption);
                EventLogStore.Append(_symbol, "15Min", "SupplyZoneRebound", "Baja", caption, justClosed.Close,
                    $"Proximal={zone.Proximal:F2};Distal={zone.Distal:F2}");
            }
        }
    }

    // Piso/Techo auto-analysis (1h panel, once per app instance — see s_pisoTechoAnalyzed).
    // Evaluated independently for the (20,40) and (100,200) SMA pairs, each against the last
    // closed hourly candle (yesterday's close, since this only runs pre-market) and that candle's
    // own Close price. Alignment comes from the PAIR (fast vs slow), but whether each individual
    // SMA still counts as Piso/Techo depends on price vs THAT SMA specifically — not just the
    // fast one — since price can open between the two:
    //   Bearish alignment (SMA_fast < SMA_slow): a given SMA is Techo only if price is still
    //     below IT. Price below both -> both Techo. Price between them -> only the slow one is
    //     still Techo (the fast one has already been broken through -> nothing).
    //   Bullish alignment (SMA_fast > SMA_slow): a given SMA is Piso only if price is still above
    //     IT. Symmetric to the above.
    //   Anything else -> no signal for that SMA. Draws via markPisoTecho, which then tracks each
    // SMA's live position on every repaint (chart.html's smaLastPoint) — no further updates
    // needed from here.
    private async Task EvaluatePisoTechoOnce()
    {
        // Compute only the very first time (this app instance, this process's lifetime); every
        // later call (chart closed and reopened the same day) just redraws whatever was already
        // decided, without re-analyzing.
        if (!s_pisoTechoAnalyzed)
        {
            s_pisoTechoAnalyzed = true;

            var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
            if (nowEastern.TimeOfDay < new TimeSpan(9, 30, 0)) // only meaningful pre-market
            {
                (s_pisoTechoResult20, s_pisoTechoResult40)   = EvaluatePisoTechoPair(20, 40);
                (s_pisoTechoResult100, s_pisoTechoResult200) = EvaluatePisoTechoPair(100, 200);

                ArmPisoTechoWatch(20, s_pisoTechoResult20);
                ArmPisoTechoWatch(40, s_pisoTechoResult40);
                ArmPisoTechoWatch(100, s_pisoTechoResult100);
                ArmPisoTechoWatch(200, s_pisoTechoResult200);

                FirePisoTechoLevelReady(20, s_pisoTechoResult20);
                FirePisoTechoLevelReady(40, s_pisoTechoResult40);
                FirePisoTechoLevelReady(100, s_pisoTechoResult100);
                FirePisoTechoLevelReady(200, s_pisoTechoResult200);

                EvaluateFirstReboundLabel();
            }
        }

        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(20, {ToJsStringOrNull(s_pisoTechoResult20)}, 40, {ToJsStringOrNull(s_pisoTechoResult40)});");
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"markPisoTecho(100, {ToJsStringOrNull(s_pisoTechoResult100)}, 200, {ToJsStringOrNull(s_pisoTechoResult200)});");
    }

    // Returns (fastResult, slowResult) — each independently "Piso", "Techo", or null.
    private (string? FastResult, string? SlowResult) EvaluatePisoTechoPair(int fastPeriod, int slowPeriod)
    {
        var fast = Sma(fastPeriod, _closedCandles.Count - 1);
        var slow = Sma(slowPeriod, _closedCandles.Count - 1);
        if (fast == null || slow == null || fast == slow) return (null, null);

        var price = _closedCandles[^1].Close;
        var bearish = fast < slow;
        return (EvaluateSingleSmaPisoTecho(fast.Value, price, bearish), EvaluateSingleSmaPisoTecho(slow.Value, price, bearish));
    }

    private static string? EvaluateSingleSmaPisoTecho(decimal sma, decimal price, bool bearishAlignment) =>
        bearishAlignment ? (price < sma ? "Techo" : null) : (price > sma ? "Piso" : null);

    // Arms a single SMA period's watch when it came back Piso or Techo — no-op if it didn't
    // (result == null). Each period is independent now (see EvaluatePisoTechoPair) — within the
    // same pair, one period can end up armed while the other isn't.
    private static void ArmPisoTechoWatch(int period, string? result)
    {
        if (result == null) return;
        s_pisoTechoWatches.Add(new PisoTechoWatch { Period = period, WatchingUp = result == "Techo" });
        if (period == 20 && result == "Techo") s_sma20TechoTouched = false;
    }

    // No-op if that period didn't come back Piso/Techo — otherwise fires OnPisoTechoLevelReadyEvent
    // with the SMA's current price, for MultiChartForm to draw the reference line on panels 2/3.
    private void FirePisoTechoLevelReady(int period, string? result)
    {
        if (result == null) return;
        var sma = Sma(period, _closedCandles.Count - 1);
        if (sma == null) return;
        OnPisoTechoLevelReadyEvent?.Invoke(period, sma.Value);
    }

    // Re-fires OnPisoTechoLevelReadyEvent for whichever periods already resolved Piso/Techo —
    // covers the race where EvaluatePisoTechoOnce (triggered by HandleCreated, right when this
    // panel is added to its parent form) finishes and fires the event BEFORE MultiChartForm gets a
    // chance to subscribe to it (subscription happens later in its constructor, after all 3 panels
    // are already added/handle-created). MultiChartForm calls this immediately after subscribing —
    // a safe no-op via FirePisoTechoLevelReady's own null-result guard if nothing resolved yet.
    public void ReplayPisoTechoLevels()
    {
        FirePisoTechoLevelReady(20, s_pisoTechoResult20);
        FirePisoTechoLevelReady(40, s_pisoTechoResult40);
        FirePisoTechoLevelReady(100, s_pisoTechoResult100);
        FirePisoTechoLevelReady(200, s_pisoTechoResult200);
    }

    // Runs once at market open (see Streamer_OnNewCandle) — a Piso already broken by a gap-down
    // open (price below it) or a Techo already broken by a gap-up open (price above it) no longer
    // means anything, so its label is removed and its watch unarmed. Checked independently for
    // each of the 4 SMAs, same as the rest of this feature.
    private void ValidatePisoTechoAgainstOpen(decimal openPrice)
    {
        if (s_pisoTechoOpenValidated) return;
        s_pisoTechoOpenValidated = true;

        InvalidateIfBrokenByOpen(20, ref s_pisoTechoResult20, openPrice);
        InvalidateIfBrokenByOpen(40, ref s_pisoTechoResult40, openPrice);
        InvalidateIfBrokenByOpen(100, ref s_pisoTechoResult100, openPrice);
        InvalidateIfBrokenByOpen(200, ref s_pisoTechoResult200, openPrice);
    }

    private void InvalidateIfBrokenByOpen(int period, ref string? result, decimal openPrice)
    {
        if (result == null) return;

        var sma = Sma(period, _closedCandles.Count - 1);
        if (sma == null) return;

        var broken = result == "Piso" ? openPrice < sma.Value : openPrice > sma.Value;
        if (!broken) return;

        result = null;
        s_pisoTechoWatches.RemoveAll(w => w.Period == period);
        // BeginInvoke — this can run from Streamer_OnNewCandle's background (WebSocket) thread
        // (via ValidatePisoTechoAgainstLivePrice, the continuous premarket check), and a direct
        // ExecuteScriptAsync call from that thread silently fails, same threading bug the PM
        // indicator had — the label never actually disappeared even once C# correctly detected the
        // invalidation.
        if (IsHandleCreated)
            BeginInvoke(async () => await (_webView.CoreWebView2?.ExecuteScriptAsync($"removePisoTechoLabel({period});") ?? Task.CompletedTask));
        OnPisoTechoLevelRemovedEvent?.Invoke(period);
        if (period == 20 || period == 40) EvaluateFirstReboundLabel();
    }

    // Continuous premarket counterpart to ValidatePisoTechoAgainstOpen — that one only checks ONCE,
    // against the actual 9:30 RTH open. This runs on every premarket tick instead (same tick that
    // feeds the blue premarket line), so a Techo the live price already trades above (or a Piso it
    // already trades below) gets invalidated the moment that happens, instead of waiting for the
    // open. Safe to call repeatedly — InvalidateIfBrokenByOpen is a no-op per period once its
    // result is already null, and by the time the real 9:30 open candle arrives
    // ValidatePisoTechoAgainstOpen just finds nothing left to invalidate for whichever periods
    // already got caught here.
    private void ValidatePisoTechoAgainstLivePrice(decimal livePrice)
    {
        InvalidateIfBrokenByOpen(20, ref s_pisoTechoResult20, livePrice);
        InvalidateIfBrokenByOpen(40, ref s_pisoTechoResult40, livePrice);
        InvalidateIfBrokenByOpen(100, ref s_pisoTechoResult100, livePrice);
        InvalidateIfBrokenByOpen(200, ref s_pisoTechoResult200, livePrice);
    }

    // The LAST hourly RTH bucket (15:00-16:00) never gets the normal "next bucket started" trigger
    // that closes and evaluates every other candle (see the sameDay branch in Streamer_OnNewCandle)
    // — RTH ends exactly when this bucket would close, so no further tick ever arrives today to
    // notice it's done. Previously this candle only got evaluated retroactively the NEXT time the
    // chart opens (LoadHistoryAsync's "yesterday's last bar" branch) — too late to act on same-day.
    // Fires once, right before close (15:59), treating the still-forming bucket as if it had just
    // closed: adds it to _closedCandles (for real this time — nothing else will) and runs the exact
    // same EvaluatePisoTechoWatches used for every other hourly candle, so a genuine Cruce/Rebote in
    // this last hour still gets logged/notified today instead of silently never firing.
    private bool _lastHourCandleEvaluated;

    private void EvaluateLastHourCandleBeforeCloseIfNeeded(DateTime eastern)
    {
        if (_mode != ChartPanelMode.Hourly15 || _lastHourCandleEvaluated || _liveBucket == null) return;
        if (eastern.TimeOfDay < new TimeSpan(15, 59, 0) || eastern.TimeOfDay >= new TimeSpan(16, 0, 0)) return;

        _lastHourCandleEvaluated = true;

        // A snapshot copy, not the live reference — _liveBucket keeps mutating (High/Low/Close)
        // with further ticks until the session actually ends at 16:00, and _closedCandles must
        // hold an immutable historical bar from here on, same as every other entry in it.
        var snapshot = new CandleData
        {
            Time = _liveBucket.Time, Open = _liveBucket.Open,
            High = _liveBucket.High, Low = _liveBucket.Low, Close = _liveBucket.Close
        };
        _closedCandles.Add(snapshot);
        EvaluatePisoTechoWatches(snapshot);
    }

    // All-Time High: recolors the reference line while the live price is trading above the stored
    // value (all 3 panels, premarket + RTH alike, purely visual), tracks today's running high
    // (1h panel only), and triggers the once-per-day persist check at the 16:00 close.
    private void EvaluateAllTimeHighLive(decimal livePrice, DateTime eastern)
    {
        if (_athValue != null)
            BeginInvoke(async () => await MarkAllTimeHighBrokenAsync(livePrice > _athValue.Value));

        if (_mode != ChartPanelMode.Hourly15) return;

        _athTodaysHigh = _athTodaysHigh == null ? livePrice : Math.Max(_athTodaysHigh.Value, livePrice);
        EvaluateAllTimeHighAtClose(eastern);
    }

    // Fires once, right before close (15:59-16:00 ET) — same window
    // EvaluateLastHourCandleBeforeCloseIfNeeded uses, and for the same reason: CHART_EQUITY
    // typically stops ticking exactly at 16:00:00, so waiting for a tick AT OR AFTER 16:00 could
    // mean this never fires at all. Persists a new All-Time High only if today's running high
    // actually beat the stored one (or none was stored yet for this symbol), then mirrors the new
    // value onto the other 2 panels via OnAllTimeHighUpdatedEvent (MultiChartForm relays it, same
    // pattern as T-Line/H-Line draws).
    private void EvaluateAllTimeHighAtClose(DateTime eastern)
    {
        if (_mode != ChartPanelMode.Hourly15 || _athEvaluatedAtClose || _athTodaysHigh == null) return;
        if (eastern.TimeOfDay < new TimeSpan(15, 59, 0)) return;

        _athEvaluatedAtClose = true;

        if (_athValue != null && _athTodaysHigh.Value <= _athValue.Value) return;

        var newValue = _athTodaysHigh.Value;
        var today = DateOnly.FromDateTime(eastern);
        AllTimeHighStore.Save(_symbol, newValue, today);
        _athValue = newValue;
        BeginInvoke(async () => await MarkAllTimeHighAsync(newValue));
        BeginInvoke(() => OnAllTimeHighUpdatedEvent?.Invoke(newValue));
    }

    // Evaluated on every closed 1h candle (see Streamer_OnNewCandle) against each still-armed
    // PisoTechoWatch — same case-1/case-2 cross-or-bounce formula used elsewhere
    // (BounceProximityRatio), against that watch's own SMA period. Resolves once per period, then
    // stops (Done) — doesn't repeat for the rest of the day. Pushes its own screenshot to
    // Telegram, same self-contained pattern as Demand Zone, with a caption explicit about which of
    // the two outcomes fired.
    private void EvaluatePisoTechoWatches(CandleData justClosed)
    {
        foreach (var watch in s_pisoTechoWatches)
        {
            if (watch.Done) continue;

            var currentSma  = Sma(watch.Period, _closedCandles.Count - 1);
            var previousSma = Sma(watch.Period, _closedCandles.Count - 2);
            if (currentSma == null) continue;

            // "1er Rebote" label tracking — a touch counts even if it doesn't go on to resolve as a
            // full Cruce/Rebote below (see crossed/bounced), so this has to be checked unconditionally
            // here, before any of the early-outs further down.
            if (watch.Period == 20 && watch.WatchingUp && justClosed.High >= currentSma)
                s_sma20TechoTouched = true;

            var isGreen = justClosed.Close > justClosed.Open;
            var isRed   = justClosed.Close < justClosed.Open;

            // 2-point comparison — the PREVIOUS candle's close vs the
            // PREVIOUS SMA value, not this candle's own open vs its own (possibly already-moved)
            // SMA. The SMA itself can shift enough between candles that no single bar's open/close
            // straddles it, even though price has genuinely crossed — comparing consecutive points
            // catches that; comparing one bar's open to its own close-time SMA doesn't.
            var crossedByClose = previousSma != null && watch.WatchingUp
                ? isGreen && justClosed.Close > currentSma && _closedCandles[^2].Close <= previousSma
                : isRed   && justClosed.Close < currentSma && _closedCandles[^2].Close >= previousSma;

            // Gap cross: the previous candle closed on the "not yet broken" side, and THIS candle
            // opened straight through to the other side — a genuine cross that happened in the gap
            // between the two candles, which crossedByClose can miss if this candle's own Close
            // ends up back on the original side (e.g. it gapped down through a Piso at the open,
            // then recovered enough to close above it again).
            var crossedByGapOpen = previousSma != null && watch.WatchingUp
                ? justClosed.Open > currentSma && _closedCandles[^2].Close <= previousSma
                : justClosed.Open < currentSma && _closedCandles[^2].Close >= previousSma;

            var crossed = crossedByClose || crossedByGapOpen;

            var bounced = !crossed && (watch.WatchingUp
                ? justClosed.Open < currentSma && isRed &&
                    (justClosed.High > currentSma
                        ? justClosed.Close < currentSma
                        : (currentSma - justClosed.High) < BounceProximityRatio * (justClosed.High - justClosed.Close))
                : justClosed.Open > currentSma && isGreen &&
                    (justClosed.Low < currentSma
                        ? justClosed.Close > currentSma
                        : (justClosed.Low - currentSma) < BounceProximityRatio * (justClosed.Close - justClosed.Low)));

            if (!crossed && !bounced) continue;

            watch.Done = true;
            var pisoTecho = watch.WatchingUp ? "Techo" : "Piso";
            var evento    = crossed ? "Cruce" : "Rebote";
            var gapTag    = crossedByGapOpen && !crossedByClose ? " (gap)" : "";
            var caption   = $"{evento}{gapTag} en {pisoTecho} — SMA{watch.Period} — cierre {justClosed.Close:F2} (SMA{watch.Period} {currentSma.Value:F2})";
            // Telegram push for this event is now MultiChartForm's job (combined 3-chart snapshot,
            // see SendPisoTechoTelegramPushAsync) instead of this panel's own single-chart one —
            // per explicit request to only send the combined image.
            EventLogStore.Append(_symbol, "Hora", $"PisoTecho{evento}", pisoTecho, caption, justClosed.Close, $"SMA{watch.Period}={currentSma.Value:F2}");
            OnPisoTechoResolvedEvent?.Invoke(evento, pisoTecho, AppendVolatilityArmSuffix(evento, pisoTecho, caption));
        }

        EvaluateFirstReboundLabel();
    }

    // "1er Rebote: 90%" yellow label, bottom-right of the 1h panel — shown while SMA20 AND SMA40
    // are BOTH currently Techo, the SMA20 watch hasn't resolved yet (no Cruce/Rebote), and no candle
    // has touched SMA20 since it was armed (s_sma20TechoTouched). Re-evaluated after every closed
    // candle (see EvaluatePisoTechoWatches) and right when the pair first arms at premarket.
    private void EvaluateFirstReboundLabel()
    {
        // THREADING BUG (confirmed live, root cause of the 1h panel freezing exactly at the day's
        // first hourly bucket): _webView.CoreWebView2 is a COM property that can only be touched
        // from the UI thread — this method is called from EvaluatePisoTechoWatches, itself called
        // from Streamer_OnNewCandle's background (WebSocket/relay) thread. The null-check used to
        // run OUTSIDE the BeginInvoke below (only the ExecuteScriptAsync call was wrapped), so it
        // threw "CoreWebView2 can only be accessed from the UI thread" on every single call from
        // there — and since this runs BEFORE Streamer_OnNewCandle's own _liveBucket/_liveBucketIndex
        // reassignment for that tick, the exception (previously silently swallowed by HandleMessage's
        // catch-all, now caught per-subscriber and logged — see RaiseOnNewCandle) meant the bucket
        // never advanced past whatever it was on the very first call, forever. The entire method
        // body — including the guard — now runs inside BeginInvoke.
        if (_mode != ChartPanelMode.Hourly15) return;

        BeginInvoke(async () =>
        {
            if (_webView.CoreWebView2 == null) return;
            var watch20 = s_pisoTechoWatches.FirstOrDefault(w => w.Period == 20);
            var show = s_pisoTechoResult20 == "Techo" && s_pisoTechoResult40 == "Techo"
                && watch20 is { Done: false } && !s_sma20TechoTouched;
            await _webView.CoreWebView2.ExecuteScriptAsync($"updateFirstRebound({(show ? "true" : "false")});");
        });
    }

    // Live-tick counterpart to EvaluatePisoTechoWatches' crossedByGapOpen — that one only fires
    // once the gapping candle itself CLOSES, per request this now fires the moment the live price
    // makes it obvious mid-candle, using the SAME comparison the chart itself draws in real time:
    // the SMA recalculated with the CURRENT live price as its newest point (LiveSma), not the
    // stale previous-candle SMA. Evaluated on every tick for the still-forming candle — its Open
    // never changes, so this settles into either firing early (as soon as the live SMA moves far
    // enough) or never, well before the candle actually closes.
    private void EvaluatePisoTechoGapLive(decimal livePrice)
    {
        if (_liveBucket == null || _closedCandles.Count == 0) return;

        foreach (var watch in s_pisoTechoWatches)
        {
            if (watch.Done) continue;

            var previousSma = Sma(watch.Period, _closedCandles.Count - 1);
            var liveSma = LiveSma(watch.Period, livePrice);
            if (previousSma == null || liveSma == null) continue;

            var lastClosed = _closedCandles[^1];
            var crossedByGapLive = watch.WatchingUp
                ? _liveBucket.Open > liveSma.Value && lastClosed.Close <= previousSma.Value
                : _liveBucket.Open < liveSma.Value && lastClosed.Close >= previousSma.Value;

            if (!crossedByGapLive) continue;

            watch.Done = true;
            var pisoTecho = watch.WatchingUp ? "Techo" : "Piso";
            var caption = $"Cruce (gap) en {pisoTecho} — SMA{watch.Period} — Open {_liveBucket.Open:F2} (SMA{watch.Period} en vivo {liveSma.Value:F2})";
            // See EvaluatePisoTechoWatches — Telegram push is MultiChartForm's job now, combined image only.
            EventLogStore.Append(_symbol, "Hora", "PisoTechoCruce", pisoTecho, caption, livePrice, $"SMA{watch.Period}={liveSma.Value:F2}");
            OnPisoTechoResolvedEvent?.Invoke("Cruce", pisoTecho, AppendVolatilityArmSuffix("Cruce", pisoTecho, caption));
        }
    }

    // Same SMA window as Sma(period, endIndex), but the newest point is the LIVE price of the
    // still-forming candle instead of a closed candle's close — i.e. exactly what the chart itself
    // is drawing at this instant, before that candle has actually closed.
    private decimal? LiveSma(int period, decimal livePrice)
    {
        if (_closedCandles.Count < period - 1) return null;
        decimal sum = livePrice;
        for (int i = _closedCandles.Count - (period - 1); i < _closedCandles.Count; i++)
            sum += _closedCandles[i].Close;
        return sum / period;
    }

    private static string ToJsStringOrNull(string? value) => value == null ? "null" : $"'{value}'";

    // ==================================================================================
    // "Abriendo la Volatilidad" (15m RTH panel only) — armed BOTH ways by default the moment RTH
    // starts (first tick >= 9:30 AM ET, see ArmVolatilityOpeningWatchDefault), so it's evaluated
    // from market open even with no prior signal. Can also be armed (one side at a time, additively)
    // externally via ArmVolatilityOpeningWatch by MultiChartForm when the 1h panel resolves a
    // Cruce/Rebote — harmless if the default watch already armed both sides. From then on, evaluated
    // on every LIVE tick (not candle close — see UpdateLivePriceFromExternalSource) against the
    // Bollinger Bands computed from this panel's own closed 15m candles: fires once the band width
    // is wider than it was a few candles ago (confirming genuine expansion, not a flat/contracting
    // band) AND the SMA20 (the Bollinger middle band) is tilted in an armed direction — NOT once
    // price physically touches a band, which was too late/restrictive. Whichever side (Superior via
    // SMA20 rising / Inferior via SMA20 falling) confirms first wins, one-shot per session. Bollinger
    // is computed here in C# purely for this detection — chart.html's own copy (for drawing) is
    // separate and untouched.
    // ==================================================================================

    private const int VolatilityBollingerPeriod = 20;
    private const decimal VolatilityBollingerMult = 2m;
    private const int VolatilityWidthLookback = 3; // candles back to compare band width against

    private bool _volatilityOpeningArmedUpper;
    private bool _volatilityOpeningArmedLower;
    private bool _volatilityOpeningFired;
    private bool _volatilityOpeningDefaultArmed; // guards the automatic 9:30 AM arm-both-bands — once per session

    // Fires with a human-readable caption once "Abriendo la Volatilidad" is confirmed.
    public event Action<string>? OnVolatilityOpeningEvent;

    // Informational only — fires once, right when the watch is armed, if the Bollinger Bands
    // already show widening AT THAT INSTANT (before any tick has even been checked against them).
    // Doesn't wait for the spot to touch a band — that's still required for OnVolatilityOpeningEvent
    // itself; this is purely a heads-up that the "bands widening" half of the condition is already
    // satisfied, so from here it's just a matter of price reaching the band. Log-only (crossLog),
    // no Telegram/EventLogStore — those still only fire once the real event confirms.
    public event Action<string>? OnVolatilityAlreadyOpenEvent;

    public void ArmVolatilityOpeningWatch(bool bullish)
    {
        if (_volatilityOpeningFired) return; // already fired once this session — don't rearm
        if (bullish) _volatilityOpeningArmedUpper = true; else _volatilityOpeningArmedLower = true;

        var current = BollingerBandsAt(_closedCandles.Count - 1);
        var earlier = BollingerBandsAt(_closedCandles.Count - 1 - VolatilityWidthLookback);
        if (current == null || earlier == null) return;

        var currentWidth = current.Value.Upper - current.Value.Lower;
        var earlierWidth = earlier.Value.Upper - earlier.Value.Lower;
        if (currentWidth <= earlierWidth) return; // not open yet at arm time — nothing to report

        var bandLabel = bullish ? "Superior" : "Inferior";
        var caption = $"Bandas de Bollinger ya abiertas al armar — ancho {currentWidth:F2} (vs {earlierWidth:F2} hace {VolatilityWidthLookback} velas) — esperando que el spot toque la Banda {bandLabel}";
        OnVolatilityAlreadyOpenEvent?.Invoke(caption);
    }

    // Called once per session, on the first RTH tick (>= 9:30 AM ET) on the 15m RTH panel — arms
    // BOTH bands by default so "Abriendo la Volatilidad" is evaluated from market open even with no
    // prior Cruce/Rebote signal. Whichever band's condition (widening + price touch) confirms first
    // fires and the other side is dropped (_volatilityOpeningFired blocks further evaluation). A
    // real Cruce/Rebote via ArmVolatilityOpeningWatch can still arrive before either side fires —
    // harmless, since both directions are already armed at that point.
    private void ArmVolatilityOpeningWatchDefault()
    {
        if (_volatilityOpeningDefaultArmed || _volatilityOpeningFired) return;
        _volatilityOpeningDefaultArmed = true;
        _volatilityOpeningArmedUpper = true;
        _volatilityOpeningArmedLower = true;
    }

    // Shared by every Piso/Techo resolution path (close-based Cruce/Rebote, live gap-cross) — the
    // rule for which direction "Abriendo la Volatilidad" gets armed in, kept in one place instead
    // of duplicated in MultiChartForm. Appended directly to the caption BEFORE it's fired, so the
    // crossLog line always carries it — a downstream consumer appending it separately (the
    // previous design) turned out to silently miss it for the live gap-cross path.
    private static string AppendVolatilityArmSuffix(string evento, string pisoTecho, string caption)
    {
        var bullish = pisoTecho == "Techo" ? evento == "Cruce" : evento == "Rebote";
        var direction = bullish ? "Alza" : "Baja";
        return $"{caption} — evaluando Abriendo la Volatilidad ({direction})";
    }

    private (decimal Upper, decimal Lower)? BollingerBandsAt(int endIndex)
    {
        if (endIndex < VolatilityBollingerPeriod - 1 || endIndex >= _closedCandles.Count) return null;

        decimal sum = 0;
        for (int i = endIndex - VolatilityBollingerPeriod + 1; i <= endIndex; i++)
            sum += _closedCandles[i].Close;
        var mean = sum / VolatilityBollingerPeriod;

        decimal sqSum = 0;
        for (int i = endIndex - VolatilityBollingerPeriod + 1; i <= endIndex; i++)
        {
            var d = _closedCandles[i].Close - mean;
            sqSum += d * d;
        }
        var stdDev = (decimal)Math.Sqrt((double)(sqSum / VolatilityBollingerPeriod));

        return (mean + VolatilityBollingerMult * stdDev, mean - VolatilityBollingerMult * stdDev);
    }

    // This panel's own Bollinger(20,2) position for `price` — used by MultiChartForm's premarket
    // "Expuesto en 3 charts" check (Daily + 1h + 15m RTH all breaking the SAME band side). Reuses
    // this panel's own _closedCandles (Hourly15/Fifteen_RTH only — Fifteen_Full never populates it).
    public BollingerDirection GetBollingerDirection(decimal price)
    {
        var bands = BollingerBandsAt(_closedCandles.Count - 1);
        if (bands == null) return BollingerDirection.None;
        if (price > bands.Value.Upper) return BollingerDirection.Above;
        if (price < bands.Value.Lower) return BollingerDirection.Below;
        return BollingerDirection.None;
    }

    // Daily Bollinger(20,2) position for `price` — "managed in memory": there's no dedicated Daily
    // ChartPanel, so this aggregates HourlyCandleStore's persisted history (same pipeline
    // EvaluateDailyBounce uses) into daily bars on demand, dropping today's still-forming bar.
    public static BollingerDirection GetDailyBollingerDirection(string symbol, decimal price)
    {
        var hourly = HourlyCandleStore.Load(symbol);
        var daily = CandleAggregation.AggregateToDaily(hourly);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));
        var closes = daily.Where(d => d.Date < today).Select(d => d.Candle.Close).ToList();
        if (closes.Count < VolatilityBollingerPeriod) return BollingerDirection.None;

        var window = closes.Skip(closes.Count - VolatilityBollingerPeriod).ToList();
        var mean = window.Average();
        var sqSum = window.Sum(c => (c - mean) * (c - mean));
        var stdDev = (decimal)Math.Sqrt((double)(sqSum / VolatilityBollingerPeriod));
        var upper = mean + VolatilityBollingerMult * stdDev;
        var lower = mean - VolatilityBollingerMult * stdDev;

        if (price > upper) return BollingerDirection.Above;
        if (price < lower) return BollingerDirection.Below;
        return BollingerDirection.None;
    }

    // Last `count` daily bars, aggregated from HourlyCandleStore's persisted history (same source
    // GetDailyBollingerDirection uses) — for DailyChartForm, a separate window with its own fresh
    // WebView2 (see MultiChartForm's Daily button). Toggling Daily in-place on the live 1h panel's
    // own WebView2 hit an unresolved rendering bug (candles stayed invisible until a manual
    // scroll); a brand-new page load doesn't carry over whatever state that bug depended on.
    public static List<CandleData> GetLastDailyCandles(string symbol, int count)
    {
        var hourly = HourlyCandleStore.Load(symbol);
        var daily = CandleAggregation.AggregateToDaily(hourly);
        return daily.Select(d => d.Candle).TakeLast(count).ToList();
    }

    // Fires on every premarket tick (Hourly15 panel only — see Streamer_OnNewCandle) with the
    // live price, so MultiChartForm can re-evaluate the "Expuesto en 3 charts" Bollinger check.
    public event Action<decimal>? OnPreMarketPriceUpdated;

    public async Task ShowExposureBannerAsync(string text)
    {
        if (_webView.CoreWebView2 == null) return;
        var json = JsonSerializer.Serialize(text);
        await _webView.CoreWebView2.ExecuteScriptAsync($"showExposureBanner({json});");
    }

    public async Task HideExposureBannerAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("hideExposureBanner();");
    }

    private void EvaluateVolatilityOpening(decimal livePrice)
    {
        if ((!_volatilityOpeningArmedUpper && !_volatilityOpeningArmedLower) || _volatilityOpeningFired) return;

        var current = BollingerBandsAt(_closedCandles.Count - 1);
        var earlier = BollingerBandsAt(_closedCandles.Count - 1 - VolatilityWidthLookback);
        if (current == null || earlier == null) return;

        var currentWidth = current.Value.Upper - current.Value.Lower;
        var earlierWidth = earlier.Value.Upper - earlier.Value.Lower;
        if (currentWidth <= earlierWidth) return; // bands aren't actually widening yet

        // Direction is dictated by the SMA20 (Bollinger's own middle band) tilting, not by price
        // physically touching a band — waiting for a touch was too late/restrictive. Same lookback
        // as the width comparison above, so this stays in sync with "widening" being confirmed over
        // that same window.
        var smaNow = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1);
        var smaEarlier = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1 - VolatilityWidthLookback);
        if (smaNow == null || smaEarlier == null || smaNow == smaEarlier) return; // no clear tilt yet

        bool bullish = smaNow > smaEarlier;
        if (bullish && !_volatilityOpeningArmedUpper) return;
        if (!bullish && !_volatilityOpeningArmedLower) return;

        _volatilityOpeningFired = true;
        var bandLabel = bullish ? "Superior" : "Inferior";
        var direction = bullish ? "Alza" : "Baja";
        var caption = $"Abriendo la Volatilidad — SMA20 girando a la {direction} ({smaEarlier.Value:F2} → {smaNow.Value:F2}), ancho bandas {currentWidth:F2} (vs {earlierWidth:F2} hace {VolatilityWidthLookback} velas) — spot {livePrice:F2}, Banda {bandLabel}";
        OnVolatilityOpeningEvent?.Invoke(caption);
        _ = SendChartToTelegramAsync(caption);
        EventLogStore.Append(_symbol, "15Min", "VolatilityOpening", direction, caption, livePrice,
            $"BollUpper={current.Value.Upper:F2};BollLower={current.Value.Lower:F2}");
    }

    // "BB" label — 1h and 15m RTH panels, next to "PM". Purely visual, continuous (re-evaluated on
    // every live tick, redraws/clears each time — no armed/fired state): shows while THIS panel's
    // own Bollinger Bands (each has its own copy — configureBollinger(20,2) is set up on both) are
    // CURRENTLY widening (same width comparison EvaluateVolatilityOpening itself uses on the 15m
    // RTH panel), regardless of whether a Cruce/Rebote watch happens to be armed — this is just
    // "are the bands opening right now", not the full Abriendo la Volatilidad signal (which stays
    // 15m-RTH-only). Colored to match THIS panel's own PM (SMA20 tilt), so "PM verde + BB verde"
    // reads as directional momentum AND volatility both agreeing.
    // Set once per "opening" episode (reset the moment it stops), so EvaluateBollingerWideningLabel
    // logs it exactly once instead of spamming on every tick while it holds — see the log block
    // below and OnBollingerOpeningEvent.
    private bool _bbOpeningLogged;

    // Fires (caption) the moment "BB" starts showing (PM tilted + bands opening, either in
    // aggregate or just the band on PM's own side) — MultiChartForm logs it to crossLog with a
    // timestamp. Persisted to EventLogStore in the same place, for offline review later.
    public event Action<string>? OnBollingerOpeningEvent;

    private void EvaluateBollingerWideningLabel(decimal livePrice)
    {
        if (_mode != ChartPanelMode.Fifteen_RTH && _mode != ChartPanelMode.Hourly15) return;

        var current = BollingerBandsAt(_closedCandles.Count - 1);
        var earlier = BollingerBandsAt(_closedCandles.Count - 1 - VolatilityWidthLookback);
        var smaNow = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1);
        var smaEarlier = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1 - VolatilityWidthLookback);

        bool show = false;
        bool bullish = false;
        if (current != null && earlier != null && smaNow != null && smaEarlier != null && smaNow != smaEarlier)
        {
            bullish = smaNow > smaEarlier;
            var currentWidth = current.Value.Upper - current.Value.Lower;
            var earlierWidth = earlier.Value.Upper - earlier.Value.Lower;
            var widthOpening = currentWidth > earlierWidth;

            // Counts as "opening" even if the aggregate width hasn't grown, as long as the band on
            // PM's own side moved that same direction (e.g. PM bullish and the upper band alone
            // climbed, even if the lower band climbed almost as much and kept total width flat).
            var upperOpening = bullish && current.Value.Upper > earlier.Value.Upper;
            var lowerOpening = !bullish && current.Value.Lower < earlier.Value.Lower;

            show = widthOpening || upperOpening || lowerOpening;
        }

        if (!show)
        {
            BeginInvoke(async () => await MarkBollingerWideningAsync(false, false));
            BeginInvoke(async () => await MarkBollingerDeltaAsync(false, 0));
            BeginInvoke(() => OnBollingerWideningLevelEvent?.Invoke(false, false));
            _bbOpeningLogged = false;
            return;
        }

        BeginInvoke(async () => await MarkBollingerWideningAsync(true, bullish));
        BeginInvoke(() => OnBollingerWideningLevelEvent?.Invoke(true, bullish));

        if (!_bbOpeningLogged)
        {
            _bbOpeningLogged = true;
            var direction = bullish ? "Alza" : "Baja";
            var timeframeLabel = _mode == ChartPanelMode.Hourly15 ? "Hora" : "15Min";
            var caption = $"Abriendo Bollinger con Volatilidad — PM {direction} — SMA20 {smaEarlier!.Value:F2} → {smaNow!.Value:F2} — spot {livePrice:F2}";
            EventLogStore.Append(_symbol, timeframeLabel, "BollingerOpening", direction, caption, livePrice,
                $"BollUpper={current!.Value.Upper:F2};BollLower={current.Value.Lower:F2}");
            // The .md entry (with the combined 3-chart screenshot) is MultiChartForm's job, not
            // this panel's — it's the only one that can capture all 3 charts at once. See its
            // OnBollingerOpeningEvent wiring / SaveBollingerOpeningSnapshotAsync.
            BeginInvoke(() => OnBollingerOpeningEvent?.Invoke(caption));
        }

        // "Δ" — distance from the live price to whichever band is closer, next to "BB". Only while
        // the price is still actually BETWEEN the two bands (bands widening but not broken out yet)
        // — once price crosses a band, "distance to the nearest one" stops being meaningful, so hide
        // it instead. Re-evaluated on every live tick alongside "BB" itself, same call sites.
        if (current != null && livePrice > current.Value.Lower && livePrice < current.Value.Upper)
        {
            var delta = Math.Min(current.Value.Upper - livePrice, livePrice - current.Value.Lower);
            BeginInvoke(async () => await MarkBollingerDeltaAsync(true, delta, bullish));
        }
        else
        {
            BeginInvoke(async () => await MarkBollingerDeltaAsync(false, 0));
        }
    }

    private async Task MarkBollingerWideningAsync(bool show, bool bullish)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"updateBollingerWidening({(show ? "true" : "false")}, {(bullish ? "true" : "false")});");
    }

    private async Task MarkBollingerDeltaAsync(bool show, decimal delta, bool bullish = false)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"updateBollingerDelta({(show ? "true" : "false")}, {delta.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {(bullish ? "true" : "false")});");
    }

    // ==================================================================================
    // "PM" (Punto Medio) — SMA20 slope indicator, 1h and 15m RTH panels only. Continuous (unlike
    // the one-shot "Abriendo la Volatilidad"): re-evaluated on every live tick, premarket and RTH
    // alike, and just redraws — no armed/fired state to track. Green when SMA20 is tilting up
    // (current > a few candles ago), red when tilting down. Same lookback convention as the
    // SMA-direction check in EvaluateVolatilityOpening, kept independent since this isn't tied to
    // Bollinger widening at all.
    //
    // Direction is fired as an event instead of drawn directly here — MultiChartForm listens to
    // BOTH panels' events, decides whether they currently agree (same color on both), and drives
    // the actual draw (MarkPuntoMedioAsync) on both with a shared "large" flag: bigger text when
    // both panels agree, normal size when they don't — a cross-panel decision this panel alone
    // can't make.
    // ==================================================================================

    public event Action<bool>? OnPuntoMedioLevelEvent;

    // Fires (show, bullish) every time EvaluateBollingerWideningLabel re-evaluates — MultiChartForm
    // listens to both panels' events the same way it does for PM, to detect when PM AND BB both
    // agree in color across the 1h and 15m RTH panels (see BuildSmaEventControls' alignment check),
    // for backtesting: logs the exact time that alignment happens.
    public event Action<bool, bool>? OnBollingerWideningLevelEvent;

    private void EvaluatePuntoMedioSlope()
    {
        if (_mode != ChartPanelMode.Hourly15 && _mode != ChartPanelMode.Fifteen_RTH) return;

        var smaNow = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1);
        var smaEarlier = Sma(VolatilityBollingerPeriod, _closedCandles.Count - 1 - VolatilityWidthLookback);
        if (smaNow == null || smaEarlier == null || smaNow == smaEarlier) return; // no clear tilt yet

        var bullish = smaNow > smaEarlier;
        BeginInvoke(() => OnPuntoMedioLevelEvent?.Invoke(bullish));
    }

    public async Task MarkPuntoMedioAsync(bool bullish, bool large)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"updatePuntoMedio({(bullish ? "true" : "false")}, {(large ? "true" : "false")});");
    }

    // Extrapolates the T-Line's price at any given time, not just between its 2 anchor points —
    // a trend line is meant to keep projecting forward. Falls back to p1 if the 2 points share
    // the same time (shouldn't happen — chart.html requires 2 distinct clicks).
    private static decimal TLineValueAt(long t1, decimal p1, long t2, decimal p2, long atTime)
    {
        if (t2 == t1) return p1;
        var slope = (p2 - p1) / (t2 - t1);
        return p1 + slope * (atTime - t1);
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

    // Decides WHEN to evaluate the auto-drawn prev-day High/Low (see DrawPrevDayHiLoAsync),
    // called once right after this panel's historical fetch finishes loading:
    //   - At/after 9:30 AM ET: today's RTH opening price is already in `aggregated` — draw now.
    //   - Fifteen_Full before 9:30 (no blue pre-market line on this panel): use the last loaded
    //     candle's Close (most recent price available at open) — draw now.
    //   - Hourly15/Fifteen_RTH before 9:30: deferred to the first live pre-market tick, see
    //     Streamer_OnNewCandle — that's the moment the blue pre-market line itself first gets a
    //     price, per explicit request.
    private async Task EvaluatePrevDayHiLoAsync(List<CandleData> aggregated)
    {
        if (aggregated.Count == 0) return;

        var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
        if (nowEastern.TimeOfDay < new TimeSpan(9, 30, 0))
        {
            if (_mode == ChartPanelMode.Fifteen_Full)
                await DrawPrevDayHiLoAsync(aggregated, aggregated[^1].Close);
            return; // Hourly15/Fifteen_RTH: wait for the first pre-market tick instead.
        }

        var today = DateOnly.FromDateTime(nowEastern);
        var todaysFirstBar = aggregated
            .Where(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone)) == today)
            .OrderBy(c => c.Time)
            .FirstOrDefault();

        await DrawPrevDayHiLoAsync(aggregated, todaysFirstBar?.Open ?? aggregated[^1].Close);
    }

    // Finds the most recent day strictly before today with data in `candles`, and draws its
    // High/Low as red H-Lines (see markPrevDayHiLo in chart.html) — but only the side(s)
    // `referencePrice` hasn't already broken through (e.g. skips the High line on a gap-open
    // above yesterday's high). Fires at most once per chart open (see _drewPrevDayHiLo).
    private async Task DrawPrevDayHiLoAsync(List<CandleData> candles, decimal referencePrice)
    {
        if (_drewPrevDayHiLo || candles.Count == 0 || _webView.CoreWebView2 == null) return;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));
        var byDate = candles
            .Select(c => (Candle: c, Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone))))
            .Where(x => x.Date < today)
            .ToList();
        if (byDate.Count == 0) return;

        var prevDate = byDate.Max(x => x.Date);
        var prevDayBars = byDate.Where(x => x.Date == prevDate).Select(x => x.Candle).ToList();

        var highBar = prevDayBars.OrderByDescending(c => c.High).First();
        var lowBar  = prevDayBars.OrderBy(c => c.Low).First();

        var drawHigh = referencePrice < highBar.High;
        var drawLow  = referencePrice > lowBar.Low;
        _drewPrevDayHiLo = true;
        OnPrevDayHiLoDebugEvent?.Invoke(
            $"{ModeLabel(_mode)}: prevDate={prevDate} high={highBar.High:F2} low={lowBar.Low:F2} ref={referencePrice:F2} drawHigh={drawHigh} drawLow={drawLow}");
        if (!drawHigh && !drawLow) return;

        // Must use the same "ET wall-clock digits disguised as UTC" epoch ToChartJson uses for
        // every candle sent to this chart (see FakeUtcEpochSeconds) — using the real UTC epoch
        // here, as this used to, put the H-Line's anchor time hours away from where that candle
        // actually sits in the chart's own (disguised) timeline, so timeToCoordinate found nothing
        // there and the line silently failed to render (intermittently "worked" only when the
        // mismatch happened to land near another real bar).
        static string TimeArg(CandleData c) => FakeUtcEpochSeconds(c.Time).ToString();
        static string PriceArg(decimal p) => p.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var highTimeJs  = drawHigh ? TimeArg(highBar) : "null";
        var highPriceJs = drawHigh ? PriceArg(highBar.High) : "null";
        var lowTimeJs   = drawLow ? TimeArg(lowBar) : "null";
        var lowPriceJs  = drawLow ? PriceArg(lowBar.Low) : "null";

        await _webView.CoreWebView2.ExecuteScriptAsync($"markPrevDayHiLo({highTimeJs}, {highPriceJs}, {lowTimeJs}, {lowPriceJs});");
    }

    // Dashed red reference line at the previous day's close — all 3 panels. Unlike the Hi/Lo
    // H-Lines above, this is a plain always-shown reference (no "already broken" check, no
    // premarket-timing deferral) — a built-in price line (see markPrevDayClose in chart.html), so
    // it's safe to call once per chart open with no extra state to track.
    private async Task DrawPrevDayCloseAsync(List<CandleData> candles)
    {
        if (candles.Count == 0 || _webView.CoreWebView2 == null) return;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));
        var byDate = candles
            .Select(c => (Candle: c, Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone))))
            .Where(x => x.Date < today)
            .ToList();
        if (byDate.Count == 0) return;

        var prevDate = byDate.Max(x => x.Date);
        var lastBar = byDate.Where(x => x.Date == prevDate).OrderBy(x => x.Candle.Time).Last().Candle;

        var timeArg  = FakeUtcEpochSeconds(lastBar.Time);
        var priceStr = lastBar.Close.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markPrevDayClose({timeArg}, {priceStr});");
    }

    // Loads the WebView2 + historical seed only — connecting/subscribing the shared streamer is
    // MultiChartForm's job (once for all 3 panels), not each panel's.
    private async Task LoadHistoryAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();

            if (!_processFailedHandlerAttached)
            {
                _processFailedHandlerAttached = true;
                _webView.CoreWebView2.ProcessFailed += (s, e) =>
                {
                    if (_closing || _crashReloadInProgress) return;
                    _crashReloadInProgress = true;
                    DebugLog($"CoreWebView2.ProcessFailed: symbol={_symbol} mode={_mode} kind={e.ProcessFailedKind} reason={e.Reason} — reloading panel");
                    BeginInvoke(async () =>
                    {
                        try { await LoadHistoryAsync(); }
                        finally { _crashReloadInProgress = false; }
                    });
                };
            }

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
                await _webView.CoreWebView2.ExecuteScriptAsync("configureSmas([20,40,100,200]);");
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");
                // Day dividers on by default (matches MultiChartForm's "Día" checkbox starting checked).
                await _webView.CoreWebView2.ExecuteScriptAsync("enableDayDividers();");

                // T-Line + vertical-arrow persistence (per symbol) — reload whatever was drawn in
                // a previous session so it reappears at the same point, and listen for new/
                // deleted/moved ones from now on so they get saved too.
                if (!_webMessageHandlerAttached)
                {
                    _webMessageHandlerAttached = true;
                    _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }

                var savedLines = TLineStore.Load(_symbol);
                if (savedLines.Count > 0)
                {
                    var linesJson = JsonSerializer.Serialize(savedLines.Select(l => new { t1 = l.T1, p1 = l.P1, t2 = l.T2, p2 = l.P2 }));
                    await _webView.CoreWebView2.ExecuteScriptAsync($"loadTLines({linesJson});");
                    UpdateTLineHint(savedLines[0].P1, savedLines[0].P2);
                }

                var savedArrows = VerticalArrowStore.Load(_symbol);
                if (savedArrows.Count > 0)
                {
                    var arrowsJson = JsonSerializer.Serialize(savedArrows.Select(a => new { time = a.Time, price = a.Price, up = a.Up }));
                    await _webView.CoreWebView2.ExecuteScriptAsync($"loadArrows({arrowsJson});");
                }

                var savedRects = RectGrisStore.Load(_symbol);
                if (savedRects.Count > 0)
                {
                    var rectsJson = JsonSerializer.Serialize(savedRects.Select(r => new { t1 = r.T1, p1 = r.P1, t2 = r.T2, p2 = r.P2 }));
                    await _webView.CoreWebView2.ExecuteScriptAsync($"loadRectGris({rectsJson});");
                }
            }

            // Bollinger Bands (20, 2 std devs) — 15m RTH panel only (1h gets its own copy above).
            // Also listens for "strike_delete" (see below) — needed here too now that Stk lines
            // can be deleted from ANY of the 3 panels, not just 1h/RTH+Overnight.
            if (_mode == ChartPanelMode.Fifteen_RTH)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");

                // White markers over the current upper/lower Bollinger band values, bounded to the
                // forming candle's width — same marker the Simulator already draws, per explicit
                // request extending it to the live app.
                await _webView.CoreWebView2.ExecuteScriptAsync("enableBollingerEdgeMarkers();");

                if (!_webMessageHandlerAttached)
                {
                    _webMessageHandlerAttached = true;
                    _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }
            }

            // Pre-market blue line (1h and 15m RTH panels): only if the chart is opened before
            // 9:30 AM ET that day — anchors at whatever candle is currently the last one loaded
            // (yesterday's close) and tracks live price until the market opens, then freezes (see
            // Streamer_OnNewCandle). Not persisted; a later re-open restarts the whole thing.
            if (_mode == ChartPanelMode.Fifteen_RTH || _mode == ChartPanelMode.Hourly15)
            {
                var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
                if (nowEastern.TimeOfDay < new TimeSpan(9, 30, 0))
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");
                }
                else if (s_preMarketLineState.TryGetValue($"{_symbol}_{_mode}", out var savedLine)
                         && savedLine.Date == DateOnly.FromDateTime(nowEastern))
                {
                    // Reopened mid-RTH-session: redraw the line/text exactly as frozen at market
                    // open instead of losing them (see s_preMarketLineState's comment).
                    var savedExposedArg = savedLine.Exposed switch
                    {
                        BollingerDirection.Above => "'above'",
                        BollingerDirection.Below => "'below'",
                        _ => "null"
                    };
                    await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        $"updatePreMarketLine({savedLine.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {savedExposedArg});");
                }
            }

            // All-Time High reference line — all 3 panels, loaded from disk if this symbol has one
            // saved yet (see AllTimeHighStore).
            var savedAth = AllTimeHighStore.Load(_symbol);
            if (savedAth != null)
            {
                _athValue = savedAth.Value.Value;
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"markAllTimeHigh({savedAth.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
            }

            // Gray shading for overnight/weekend gaps — only on the 15m RTH+Overnight panel.
            // Also listens for "dzsz" messages (Demand Zone rebote detection, see
            // EvaluateDemandZoneRebounds) — this panel is the only one with DZ/SZ enabled.
            if (_mode == ChartPanelMode.Fifteen_Full)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureOvernightBands();");
                if (!_webMessageHandlerAttached)
                {
                    _webMessageHandlerAttached = true;
                    _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                }
            }

            // Default zoom on open: 1h panel shows the last 7 days, the two 15m panels show the
            // last 3 — full history is still loaded underneath for SMA/Bollinger, this only
            // limits the initial visible window (user can still scroll/zoom out manually).
            var visibleDays = _mode == ChartPanelMode.Hourly15 ? 7 : 3;
            await _webView.CoreWebView2.ExecuteScriptAsync($"configureVisibleDays({visibleDays});");

            // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day.
            // 1h panel shows the full 10 days; the two 15m panels show the last 3 days.
            var requestDays = _mode == ChartPanelMode.Hourly15 ? 10 : 3;
            var history = await _historyClient.GetHistoricalCandlesAsync(_symbol, requestDays);
            if (history.Count > 0)
            {
                var filtered = CandleAggregation.FilterSession(history, _rthOnly);
                _rawHistory = filtered; // cached so ToggleIntervalAsync can re-aggregate without re-fetching

                // 1h panel: first bucket of the session is 9:30-10:00, every one after that is
                // aligned to the clock hour (10-11, 11-12, ...) instead of floating 60-min offsets
                // from 9:30 — the two 15m panels keep the regular fixed-interval bucketing.
                var aggregated = _mode == ChartPanelMode.Hourly15
                    ? CandleAggregation.AggregateToHourlyRthBuckets(filtered)
                    : CandleAggregation.AggregateToInterval(filtered, _intervalMinutes, _rthOnly);

                // 1h panel: persist today's fetch to disk and merge with everything saved from
                // previous sessions, so SMA 100/200 can accumulate beyond Schwab's 10-day limit.
                // ReplaceDates (not AppendIfMissing) because these bars use the new hour-aligned
                // bucketing above — for any day covered by this fetch, it replaces whatever was
                // already stored for that day instead of merging by exact Time, so old bars from
                // the previous (9:30-anchored) bucketing don't linger alongside the new ones.
                if (_mode == ChartPanelMode.Hourly15 && aggregated.Count > 0)
                {
                    HourlyCandleStore.ReplaceDates(_symbol, aggregated);
                    aggregated = HourlyCandleStore.Load(_symbol);
                    EvaluateDailyBounce(aggregated);
                }

                if (aggregated.Count > 0)
                {
                    await RunScriptAsync("loadHistory", aggregated);
                    // Seed the live aggregator with the last historical bucket so the first live
                    // tick extends it correctly instead of starting a spurious new one.
                    var last = aggregated[^1];
                    if (_mode == ChartPanelMode.Hourly15)
                    {
                        _liveBucketIndex = CandleAggregation.HourlyRthBucketKey(last.Time);
                    }
                    else
                    {
                        _liveAnchor      = CandleAggregation.BucketAnchor(new[] { last }, _rthOnly);
                        _liveBucketIndex = CandleAggregation.BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
                    }
                    _liveBucket      = last;

                    // Seed Cross-SMA monitoring's closed-candle history — everything fetched here
                    // is already closed (it's historical data); the live aggregator (above) owns
                    // the currently-forming candle separately.
                    if (_mode == ChartPanelMode.Hourly15)
                    {
                        _closedCandles.Clear();
                        _closedCandles.AddRange(aggregated);

                        await EvaluatePisoTechoOnce();

                        // Yesterday's LAST hourly candle (15:00-16:00) never gets a same-day
                        // follow-up tick to close it live (see Streamer_OnNewCandle's sameDay
                        // check) — evaluate it once here instead, on open, same idea as
                        // EvaluateDailyBounce. Only for a PRIOR day's bar — if this instance is
                        // reopened mid-session, the last bar is today's and already got evaluated
                        // live when it originally closed, so re-checking it here would just be a
                        // duplicate (and, for the T-Line signal, a duplicate Telegram push).
                        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone));
                        var lastBarDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(last.Time, EasternZone));
                        if (lastBarDate < today)
                        {
                            EvaluateTLineSignal(last);
                            EvaluatePisoTechoWatches(last);
                        }
                    }
                    else if (_mode == ChartPanelMode.Fifteen_RTH)
                    {
                        // Own closed-candle history for this panel's Bollinger-widening watch
                        // (EvaluateVolatilityOpening) — armed externally by MultiChartForm when the
                        // 1h panel resolves a Cruce en Techo.
                        _closedCandles.Clear();
                        _closedCandles.AddRange(aggregated);
                    }

                    await EvaluatePrevDayHiLoAsync(aggregated);
                    await DrawPrevDayCloseAsync(aggregated);
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
        RestoreHeaderIfWasDisconnected();

        var eastern = TimeZoneInfo.ConvertTimeFromUtc(candle.Time, EasternZone);
        OnLiveTick?.Invoke(eastern, candle.Close);
        EvaluatePuntoMedioSlope(); // premarket + RTH alike, see method comment
        EvaluateLastHourCandleBeforeCloseIfNeeded(eastern);
        EvaluateAllTimeHighLive(candle.Close, eastern); // all 3 panels, premarket + RTH alike
        if (_mode == ChartPanelMode.Fifteen_RTH && eastern.TimeOfDay >= new TimeSpan(9, 30, 0))
            ArmVolatilityOpeningWatchDefault();
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
        {
            // Pre-market tick on the 1h/15m RTH panels — doesn't form a candle, but feeds the blue
            // pre-market line (if startPreMarketLine was called when this panel opened). Once
            // 9:30 AM ET hits this branch stops firing for that reason, which is what freezes the
            // line in place with no extra "freeze" logic needed.
            if ((_mode == ChartPanelMode.Fifteen_RTH || _mode == ChartPanelMode.Hourly15) && eastern.TimeOfDay < new TimeSpan(9, 30, 0))
            {
                var price = candle.Close;

                // Blue premarket line as an extra live validation of Piso/Techo, ahead of the
                // 9:30 open check — see ValidatePisoTechoAgainstLivePrice.
                if (_mode == ChartPanelMode.Hourly15) ValidatePisoTechoAgainstLivePrice(price);

                // "BB" next to "PM" — also live during premarket now (previously only evaluated
                // once RTH ticks started forming today's bucket), so a trader watching premarket
                // already sees whether the bands are opening before the open, not just after.
                EvaluateBollingerWideningLabel(price);

                // "Expuesto" text next to the blue premarket line itself — this panel's OWN
                // Bollinger(20,2) band (not the 3-chart combo check MultiChartForm does elsewhere):
                // above the line if price already broke the upper band, below it if it broke the
                // lower one, hidden otherwise.
                var exposedDir = GetBollingerDirection(price);
                var exposedArg = exposedDir switch
                {
                    BollingerDirection.Above => "'above'",
                    BollingerDirection.Below => "'below'",
                    _ => "null"
                };
                s_preMarketLineState[$"{_symbol}_{_mode}"] = (DateOnly.FromDateTime(eastern), price, exposedDir);

                BeginInvoke(async () =>
                {
                    if (_webView.CoreWebView2 == null) return;
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        $"updatePreMarketLine({price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {exposedArg});");

                    // First pre-market tick this session (and only the first — DrawPrevDayHiLoAsync
                    // is itself once-only) is also this panel's first real "current price", so
                    // that's the moment the auto-drawn prev-day High/Low red lines get evaluated —
                    // deferred here instead of at chart-open per explicit request ("que se dibuje
                    // luego que se muestre la línea azul de precio premarket").
                    await DrawPrevDayHiLoAsync(_rawHistory, price);
                });

                // Only from the 1h panel (fires on every premarket tick, not just the first) —
                // MultiChartForm re-evaluates the "Expuesto en 3 charts" Bollinger check from here.
                if (_mode == ChartPanelMode.Hourly15) OnPreMarketPriceUpdated?.Invoke(price);
            }
            return; // outside this panel's session — ignore the tick entirely
        }

        // Fallback for the deferred draw above: if this panel was opened before 9:30 but the first
        // live tick it actually received already landed at/after 9:30 (connection/subscription race
        // — premarket window missed entirely), DrawPrevDayHiLoAsync above never fired. Catch it here
        // on the first RTH tick instead — still a no-op if it already fired (see _drewPrevDayHiLo).
        if ((_mode == ChartPanelMode.Fifteen_RTH || _mode == ChartPanelMode.Hourly15) && !_drewPrevDayHiLo)
        {
            var price = candle.Close;
            BeginInvoke(async () => await DrawPrevDayHiLoAsync(_rawHistory, price));
        }

        var isHourly = _mode == ChartPanelMode.Hourly15;

        // Set true below when this tick starts the FIRST hourly bucket of a new day (today's
        // market open) — that new bucket's Time is hours away from the seeded bucket's (yesterday's
        // last hourly bar), and candleSeries.update()'s incremental path doesn't handle that gap
        // correctly (visually "stretches" the bar) — same issue Fifteen_RTH's resetToNewDayCandle
        // was added for. Routes the send below to that same full-rerender path instead of the
        // normal incremental updateLastCandle.
        var isHourlyNewDayFirstBucket = false;

        if (_liveBucket == null)
        {
            if (isHourly)
            {
                _liveBucketIndex = CandleAggregation.HourlyRthBucketKey(candle.Time);
                var start = CandleAggregation.HourlyRthBucketStartUtc(candle.Time);
                _liveBucket = new CandleData { Time = start, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
            }
            else
            {
                _liveAnchor      = eastern.Date.AddHours(_rthOnly ? 9 : 0).AddMinutes(_rthOnly ? 30 : 0);
                _liveBucketIndex = CandleAggregation.BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
                _liveBucket      = new CandleData { Time = candle.Time, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
            }
        }
        else
        {
            // 15m RTH panel only: _liveAnchor is only ever set once — either at market open
            // (_liveBucket == null branch, above) or when LoadHistoryAsync seeds _liveBucket from
            // history — and never refreshed after that. BucketIndex's day-boundary separation
            // (24h = an exact multiple of any interval that divides it) should still work off a
            // stale anchor in theory, but in practice this was observed merging yesterday's last
            // seeded bucket straight into today's first live tick (a single candle spanning both
            // days' prices). Rather than trust that index math, explicitly reset for a new
            // calendar day here — unambiguous, and guarantees today's RTH candles are built
            // purely from today's live ticks, never carrying over any part of a prior day's bar.
            if (_mode == ChartPanelMode.Fifteen_RTH && _liveBucket != null)
            {
                var liveBucketDate = TimeZoneInfo.ConvertTimeFromUtc(_liveBucket.Time, EasternZone).Date;
                DebugLog($"day-reset check: symbol={_symbol} tickTime={eastern:yyyy-MM-dd HH:mm:ss} liveBucketTime={liveBucketDate:yyyy-MM-dd} willReset={eastern.Date != liveBucketDate}");
                if (eastern.Date != liveBucketDate)
                {
                    _liveAnchor      = eastern.Date.AddHours(9).AddMinutes(30);
                    _liveBucketIndex = CandleAggregation.BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
                    _liveBucket      = new CandleData { Time = candle.Time, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
                    var freshBucket = _liveBucket;
                    BeginInvoke(async () => await RunScriptAsync("resetToNewDayCandle", freshBucket));
                    return;
                }
            }

            var index = isHourly
                ? CandleAggregation.HourlyRthBucketKey(candle.Time)
                : CandleAggregation.BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
            if (index != _liveBucketIndex)
            {
                if (_mode == ChartPanelMode.Hourly15)
                {
                    _closedCandles.Add(_liveBucket);

                    // At market open, the first live tick of the day always looks like "a new
                    // bucket" compared to whatever _liveBucket was seeded with from history
                    // (LoadHistoryAsync seeds it with YESTERDAY's last hourly bar, e.g. 15:00-16:00,
                    // so live ticks extend it correctly instead of starting a spurious bucket).
                    // That seeded bucket already closed yesterday — it must NOT be evaluated again
                    // here just because today's session started; doing so previously fired T-Line
                    // events at ~9:31 AM using yesterday's stale close. Only evaluate when the
                    // outgoing bucket is from the SAME trading day as this tick (HourlyRthBucketKey
                    // = dayNumber*100+slot, so dividing by 100 isolates the day).
                    var sameDay = _liveBucketIndex is { } prevIndex && prevIndex / 100 == index / 100;
                    if (sameDay)
                    {
                        EvaluateTLineSignal(_liveBucket);
                        EvaluatePisoTechoWatches(_liveBucket);

                        // The Piso/Techo reference line (15m RTH/RTH+Overnight panels) tracks the
                        // live SMA, not just its pre-market snapshot — re-fire with each period's
                        // CURRENT value on every closed candle so MultiChartForm redraws it in
                        // place (markPisoTechoRefLine already replaces the old entry per period).
                        FirePisoTechoLevelReady(20, s_pisoTechoResult20);
                        FirePisoTechoLevelReady(40, s_pisoTechoResult40);
                        FirePisoTechoLevelReady(100, s_pisoTechoResult100);
                        FirePisoTechoLevelReady(200, s_pisoTechoResult200);
                    }
                    else
                    {
                        // This transition IS today's market open (the outgoing bucket was
                        // yesterday's) — candle.Open below is the actual RTH opening price.
                        ValidatePisoTechoAgainstOpen(candle.Open);
                        isHourlyNewDayFirstBucket = true;
                    }
                }
                else if (_mode == ChartPanelMode.Fifteen_Full)
                {
                    EvaluateDemandZoneRebounds(_liveBucket);
                    EvaluateSupplyZoneRebounds(_liveBucket);

                    // Fires on EVERY closed 15m candle while armed (a rebote just confirmed above,
                    // possibly on this very candle, or on an earlier one still in progress) —
                    // MultiChartForm captures+pushes the combined 3-chart snapshot each time.
                    if (_autoZonePushArmed) OnAutoZonePushTickEvent?.Invoke(_liveBucket);
                }
                else if (_mode == ChartPanelMode.Fifteen_RTH)
                {
                    _closedCandles.Add(_liveBucket);
                }

                _liveBucketIndex = index;
                var newTime = isHourly ? CandleAggregation.HourlyRthBucketStartUtc(candle.Time) : candle.Time;
                _liveBucket = new CandleData { Time = newTime, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
            }
            else
            {
                _liveBucket.High  = Math.Max(_liveBucket.High, candle.High);
                _liveBucket.Low   = Math.Min(_liveBucket.Low, candle.Low);
                _liveBucket.Close = candle.Close;
            }
        }

        var toSend = _liveBucket;
        var jsFunction = isHourlyNewDayFirstBucket ? "resetToNewDayCandle" : "updateLastCandle";
        BeginInvoke(async () => await RunScriptAsync(jsFunction, toSend));

        if (_mode == ChartPanelMode.Fifteen_RTH)
        {
            EvaluateVolatilityOpening(candle.Close);
            EvaluateBollingerWideningLabel(candle.Close);
        }
        else if (_mode == ChartPanelMode.Hourly15)
        {
            EvaluatePisoTechoGapLive(candle.Close);
            EvaluateBollingerWideningLabel(candle.Close);
        }
    }

    // Real-time last-price update (LEVEL_ONE_EQUITIES, much higher frequency than CHART_EQUITY's
    // 1-minute bars) — currently never fires, see SubscribeLevelOneEquity's disabled call site.
    private void Streamer_OnLevelOneTick(string symbol, decimal price, DateTime utcTime)
    {
        if (symbol != _symbol) return;
        UpdateLivePriceFromExternalSource(price, utcTime);
    }

    // Every ~6s options-chain poll cycle also carries a fresh SpotPrice (Form1's own REST polling,
    // completely separate from the streaming feed) — while LEVEL_ONE_EQUITIES is disabled, Form1
    // feeds that spot price here instead so the currently-forming candle still tracks something
    // closer to real-time than waiting a full minute for the next CHART_EQUITY bar.
    public void FeedPollingPrice(decimal price, DateTime utcTime) => UpdateLivePriceFromExternalSource(price, utcTime);

    // Only ever adjusts the CURRENTLY-forming bucket's Close (and extends High/Low if the tick
    // exceeds them) — CHART_EQUITY still owns bucket boundaries and Open, so this can't desync
    // from it, it just makes the live price shown track more recent data than waiting for the
    // next full CHART_EQUITY bar. Shared by both possible sources (LEVEL_ONE_EQUITIES ticks and
    // Form1's options-chain polling) so there's exactly one place this logic lives.
    private void UpdateLivePriceFromExternalSource(decimal price, DateTime utcTime)
    {
        if (_closing || !IsHandleCreated) return;
        RestoreHeaderIfWasDisconnected();

        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternZone);
        OnLiveTick?.Invoke(eastern, price); // fires regardless of session/bucket state, same as Streamer_OnNewCandle
        EvaluatePuntoMedioSlope(); // premarket + RTH alike, see method comment
        EvaluateLastHourCandleBeforeCloseIfNeeded(eastern);
        EvaluateAllTimeHighLive(price, eastern); // all 3 panels, premarket + RTH alike
        if (_mode == ChartPanelMode.Fifteen_RTH && eastern.TimeOfDay >= new TimeSpan(9, 30, 0))
            ArmVolatilityOpeningWatchDefault();
        if (eastern.TimeOfDay < new TimeSpan(9, 30, 0)) EvaluateBollingerWideningLabel(price); // "BB" live during premarket too

        if (_liveBucket == null) return; // no bucket open yet — CHART_EQUITY seeds the first one
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
            return; // outside this panel's session — ignore the tick entirely

        // Same day-reset guard Streamer_OnNewCandle has — this method (Form1's ~6s polling feed,
        // via FeedPollingPrice) had NONE, and could reach here first if a polling tick lands right
        // at 9:30 before the WebSocket's own first RTH tick gets a chance to reset _liveBucket:
        // yesterday's real closed bucket (still sitting in _liveBucket, Open correct) would get its
        // High/Close silently overwritten with TODAY's opening price — confirmed via real tick
        // data (yesterday's real 15:59 close ~302.71 vs the corrupted bucket's Close ~304.11,
        // which almost exactly matched today's 9:30 open ~304.07).
        if (_mode == ChartPanelMode.Fifteen_RTH)
        {
            var liveBucketDate = TimeZoneInfo.ConvertTimeFromUtc(_liveBucket.Time, EasternZone).Date;
            if (eastern.Date != liveBucketDate)
            {
                _liveAnchor      = eastern.Date.AddHours(9).AddMinutes(30);
                _liveBucketIndex = CandleAggregation.BucketIndex(utcTime, _liveAnchor, _intervalMinutes);
                _liveBucket      = new CandleData { Time = utcTime, Open = price, High = price, Low = price, Close = price };
                var freshBucket  = _liveBucket;
                BeginInvoke(async () => await RunScriptAsync("resetToNewDayCandle", freshBucket));
                return;
            }
        }

        _liveBucket.High  = Math.Max(_liveBucket.High, price);
        _liveBucket.Low   = Math.Min(_liveBucket.Low, price);
        _liveBucket.Close = price;

        var toSend = _liveBucket;
        BeginInvoke(async () => await RunScriptAsync("updateLastCandle", toSend));

        if (_mode == ChartPanelMode.Fifteen_RTH)
        {
            EvaluateVolatilityOpening(price);
            EvaluateBollingerWideningLabel(price);
        }
        else if (_mode == ChartPanelMode.Hourly15)
        {
            EvaluatePisoTechoGapLive(price);
            EvaluateBollingerWideningLabel(price);
        }
    }

    // Whether the header currently shows the "disconnected" message — cleared the moment real
    // data starts flowing again, so the header doesn't stay stuck on it forever after a silent
    // auto-reconnect (previously nothing ever reverted it).
    private bool _headerShowsDisconnected;

    private string NormalHeaderText() => _mode == ChartPanelMode.Fifteen_Full
        ? $"{_symbol} — {_intervalMinutes}m RTH+Overnight"
        : $"{_symbol} — {ModeLabel(_mode)}";

    private void RestoreHeaderIfWasDisconnected()
    {
        if (!_headerShowsDisconnected) return;
        _headerShowsDisconnected = false;
        BeginInvoke(() => _header.Text = NormalHeaderText());
    }

    private void Streamer_OnDisconnected(string message)
    {
        if (_closing || !IsHandleCreated) return;
        _headerShowsDisconnected = true;
        BeginInvoke(() => _header.Text = $"{_symbol} — {ModeLabel(_mode)} — {message}");
    }

    // Serializes the payload as JSON and calls the given JS function with it — used for both
    // loadHistory(velas[]) and updateLastCandle(vela).
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
    // Public alias — DailyChartForm needs the exact same "ET wall-clock digits disguised as UTC"
    // candle encoding for its own, separate loadHistory call, without duplicating it.
    public static string ToChartJsonPublic(object payload) => ToChartJson(payload);

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
    // out so the pre-market line's start time (not a candle) can use it too. Public so
    // MultiChartForm can compute the Piso/Techo reference line's session-start anchor the same way
    // (mirrors SimulatedChartPanel.ToFakeUtcEpochSeconds, already public for the same reason).
    public static long FakeUtcEpochSeconds(DateTime utcTime)
    {
        var easternWallClock = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
        var fakeUtcForDisplay = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Utc);
        return new DateTimeOffset(fakeUtcForDisplay).ToUnixTimeSeconds();
    }
}
