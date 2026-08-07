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
    // is decided per pair, never split). Completely separate from the manual Cross-SMA toggle
    // buttons (ChartPanel's _crossArmedPeriods/_crossActivePeriod/_crossUp) — this doesn't touch
    // or share that state, same "runs independently" precedent as EvaluateDemandZoneRebounds.
    private sealed class PisoTechoWatch
    {
        public int Period;
        public bool WatchingUp; // true = Techo (expects reject down / cross up), false = Piso (expects bounce up / cross down)
        public bool Done;
    }
    private static readonly List<PisoTechoWatch> s_pisoTechoWatches = new();

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
    private long? _liveBucketIndex;
    private DateTime _liveAnchor;

    // Cross-SMA sequence monitoring (Hourly15 panel only) — closed 1h candles kept for computing
    // SMA ourselves in C# (same simple-average formula as the JS overlay).
    //
    // The user picks which periods participate by arming their buttons (e.g. 20 and 40) — at any
    // given moment only ONE of them is "active" (the nearest one price hasn't gotten past yet).
    // Each closed candle is checked against the active period for exactly one of two outcomes:
    //   - Bounce (rejected before/at getting through) → reported, stays on the SAME active period.
    //   - Genuine cross (gets cleanly through) → reported, ADVANCES to the next armed period.
    // Once the last armed period resolves (either way), the whole sequence stops firing for the
    // rest of the session — each app instance only runs one RTH session anyway, so there's no
    // "reset" to wire up.
    private readonly List<CandleData> _closedCandles = new();
    private readonly SortedSet<int> _crossArmedPeriods = new();
    private int? _crossActivePeriod;
    private bool _crossUp;      // fixed for the whole sequence once the first period is armed
    private bool _crossFinished;

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

    // Fires once the last armed period's event has been reported — MultiChartForm uses this to
    // reset all 4 buttons back to their neutral/off appearance, since the sequence won't respond
    // to anything else for the rest of the session.
    public event Action? OnCrossSequenceFinished;

    // Fires with a human-readable message every time a cross or bounce event is detected —
    // regardless of whether the Telegram push actually succeeds — so the caller can log it
    // locally (e.g. to verify the detection logic is firing as expected).
    public event Action<string>? OnCrossSequenceEvent;

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

    // Toggles Piso/Techo text-label drawing mode on/off. While on, every click writes the given
    // orange text at that point (no pairing — one click per label). Same toggle pattern as H-Line.
    public async Task<bool> TogglePisoModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleFloorLabel();");
        return result == "true";
    }

    public async Task<bool> ToggleTechoModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleCeilingLabel();");
        return result == "true";
    }

    // Programmatic (not click-driven) red "Expired!!!" marker at the most recent candle — used
    // by the 4pm ET expiration auto-close, not exposed via any UI toggle.
    public async Task MarkExpiredAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("markExpired();");
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
    public async Task MarkPisoTechoRefLineAsync(int period, decimal price, long sessionStartFakeEpoch)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markPisoTechoRefLine({period}, {priceStr}, {sessionStartFakeEpoch});");
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
                    }
                    else
                    {
                        TLineStore.Remove(_symbol, t1, p1, t2, p2);
                        _tLineSignalFired = false;
                        _ = _webView.CoreWebView2?.ExecuteScriptAsync("setTLineHint('');");
                    }
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

    // Toggles the 1h panel between Daily and Hourly candles. All the aggregation (grouping the
    // already-loaded hourly history into one bar per day) and SMA recomputation happens entirely
    // in JS (chart.html's toggleDaily) off the same data already on the chart — no new fetch or
    // re-seed needed here, unlike ToggleIntervalAsync above. Drawings (T-Line, arrows, etc.) are
    // untouched since they're anchored to real timestamps valid in either view. Returns true if
    // now showing Daily candles.
    public async Task<bool> ToggleDailyModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleDaily();");
        return result == "true";
    }

    // Toggles whether `period` participates in the cross/bounce sequence. Arming the FIRST period
    // of a fresh sequence decides the direction for the whole sequence from where price currently
    // sits relative to that period's SMA: below it → watch for cross UP / bounce DOWN; above it →
    // the mirror (cross DOWN / bounce UP). Later periods armed while a sequence is already running
    // just join the pool of periods it can advance through — they don't re-decide direction.
    // Returns (Armed, Up) so the caller can show which direction the sequence is using.
    public (bool Armed, bool Up) ToggleCrossMonitor(int period)
    {
        if (_crossArmedPeriods.Remove(period))
        {
            if (_crossActivePeriod == period)
                AdvanceCrossSequence(period); // was watching this one — move on (or finish)
            return (false, false);
        }

        if (_crossFinished) return (false, false); // sequence already ran its course this session

        if (_crossActivePeriod == null)
        {
            // Starting a brand new sequence — decide direction from price vs. this (first) period.
            var currentPrice = _liveBucket?.Close ?? _closedCandles.LastOrDefault()?.Close;
            var currentSma   = _closedCandles.Count > 0 ? Sma(period, _closedCandles.Count - 1) : null;
            if (currentPrice == null || currentSma == null) return (false, false); // not enough data yet

            _crossUp = currentPrice < currentSma;
            _crossActivePeriod = period;
        }

        _crossArmedPeriods.Add(period);
        return (true, _crossUp);
    }

    // Moves the active period forward to the next still-armed period greater than `resolved`, or
    // ends the sequence for the rest of the session if there isn't one.
    private void AdvanceCrossSequence(int resolved)
    {
        _crossArmedPeriods.Remove(resolved);
        var next = _crossArmedPeriods.Where(p => p > resolved).OrderBy(p => p).Cast<int?>().FirstOrDefault();
        if (next == null)
        {
            _crossActivePeriod = null;
            _crossFinished = true;
            OnCrossSequenceFinished?.Invoke();
        }
        else
        {
            _crossActivePeriod = next;
        }
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

    // Evaluates the candle that just closed against whichever period is currently active in the
    // cross/bounce sequence (see the fields above) — only ONE period is ever checked per candle.
    //
    // Genuine cross (advances the sequence to the next armed period): candle color matches the
    // sequence's direction (green for UP, red for DOWN), its close ends up on the crossed side of
    // the SMA(period), AND the previous candle was still on the other side (or on the line).
    //
    // Bounce (reported, but stays on the SAME period — case 1 or case 2, mirrored for the DOWN
    // direction): price went looking for the SMA from its side and got rejected back the way it
    // came, closing red (UP direction) or green (DOWN direction) instead of getting through.
    //   Case 1 — touched/crossed intra-candle but rejected: the wick reached past the SMA, but
    //            price closed back on the original side by the close.
    //   Case 2 — didn't quite reach it: the wick fell short of the SMA, but came within
    //            BounceProximityRatio of the rejection move's size (i.e. "closely missed it").
    private void EvaluateCrossings(CandleData justClosed)
    {
        if (_crossFinished || _crossActivePeriod == null) return;

        var period = _crossActivePeriod.Value;
        if (_closedCandles.Count < period + 1) return; // not enough history for this + the prior SMA

        var currentSma  = Sma(period, _closedCandles.Count - 1);
        var previousSma = Sma(period, _closedCandles.Count - 2);
        if (currentSma == null) return;

        var isGreen = justClosed.Close > justClosed.Open;
        var isRed   = justClosed.Close < justClosed.Open;

        var crossed = previousSma != null && _crossUp
            ? isGreen && justClosed.Close > currentSma && _closedCandles[^2].Close <= previousSma
            : isRed   && justClosed.Close < currentSma && _closedCandles[^2].Close >= previousSma;

        if (crossed)
        {
            FireCrossSequenceEvent(period, "Cruce", justClosed.Close, currentSma!.Value);
            AdvanceCrossSequence(period);
            return;
        }

        var bounced = _crossUp
            ? justClosed.Open < currentSma && isRed &&
                (justClosed.High > currentSma
                    ? justClosed.Close < currentSma                                            // case 1: crossed, rejected back down
                    : (currentSma - justClosed.High) < BounceProximityRatio * (justClosed.High - justClosed.Close)) // case 2: fell short, closely
            : justClosed.Open > currentSma && isGreen &&
                (justClosed.Low < currentSma
                    ? justClosed.Close > currentSma                                             // case 1 mirrored: crossed, rejected back up
                    : (justClosed.Low - currentSma) < BounceProximityRatio * (justClosed.Close - justClosed.Low)); // case 2 mirrored

        if (bounced) FireCrossSequenceEvent(period, "Rebote", justClosed.Close, currentSma!.Value); // stays on the same active period
    }

    private void FireCrossSequenceEvent(int period, string eventLabel, decimal price, decimal smaValue)
    {
        var direction = _crossUp ? "UP" : "DOWN";
        var colorName = SmaColorNames.TryGetValue(period, out var c) ? c : string.Empty;
        var caption = $"{eventLabel} {direction} SMA {period}({colorName})";
        OnCrossSequenceEvent?.Invoke(caption);
        _ = SendChartToTelegramAsync(caption);

        var eventDirection = direction == "UP" ? "Alza" : "Baja";
        EventLogStore.Append(_symbol, "Hora", "CrossSMA", eventDirection, caption, price, $"SMA{period}={smaValue:F2}");
    }

    // Daily-candle bounce off the daily SMA20 — evaluated once per app run, right after the 1h
    // panel's history loads (only if this window is open at all; if it's closed, this never
    // runs). Checks the last already-CLOSED daily bar (yesterday — today's bar, if present in
    // `hourly`, is still forming and is excluded) against the daily SMA20, using the exact same
    // case-1/case-2 bounce formula as EvaluateCrossings (BounceProximityRatio), just on daily bars
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
    //     case-1/case-2 proximity idea as EvaluateCrossings' bounce detection: a candle whose Low
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
        _ = _webView.CoreWebView2?.ExecuteScriptAsync($"removePisoTechoLabel({period});");
        OnPisoTechoLevelRemovedEvent?.Invoke(period);
    }

    // Evaluated on every closed 1h candle (see Streamer_OnNewCandle) against each still-armed
    // PisoTechoWatch — same case-1/case-2 cross-or-bounce formula as the manual Cross-SMA
    // (EvaluateCrossings), just against that watch's own SMA period instead of the shared manual
    // sequence. Resolves once per period, then stops (Done) — doesn't repeat for the rest of the
    // day. Pushes its own screenshot to Telegram, same self-contained pattern as Cross-SMA/Demand
    // Zone, with a caption explicit about which of the two outcomes fired.
    private void EvaluatePisoTechoWatches(CandleData justClosed)
    {
        foreach (var watch in s_pisoTechoWatches)
        {
            if (watch.Done) continue;

            var currentSma  = Sma(watch.Period, _closedCandles.Count - 1);
            var previousSma = Sma(watch.Period, _closedCandles.Count - 2);
            if (currentSma == null) continue;

            var isGreen = justClosed.Close > justClosed.Open;
            var isRed   = justClosed.Close < justClosed.Open;

            // Same 2-point comparison as EvaluateCrossings — the PREVIOUS candle's close vs the
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
            _ = SendChartToTelegramAsync(caption);
            EventLogStore.Append(_symbol, "Hora", $"PisoTecho{evento}", pisoTecho, caption, justClosed.Close, $"SMA{watch.Period}={currentSma.Value:F2}");
            OnPisoTechoResolvedEvent?.Invoke(evento, pisoTecho, AppendVolatilityArmSuffix(evento, pisoTecho, caption));
        }
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
            _ = SendChartToTelegramAsync(caption);
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
    // "Abriendo la Volatilidad" (15m RTH panel only) — armed externally (ArmVolatilityOpeningWatch)
    // by MultiChartForm when the 1h panel resolves a Cruce en Techo. From then on, evaluated on
    // every LIVE tick (not candle close — see UpdateLivePriceFromExternalSource) against the
    // Bollinger Bands computed from this panel's own closed 15m candles: fires once the live spot
    // reaches the Upper Band AND that band's current width is wider than it was a few candles ago
    // (confirming genuine expansion, not a touch against a flat/contracting band). Bollinger is
    // computed here in C# purely for this detection — chart.html's own copy (for drawing) is
    // separate and untouched.
    // ==================================================================================

    private const int VolatilityBollingerPeriod = 20;
    private const decimal VolatilityBollingerMult = 2m;
    private const int VolatilityWidthLookback = 3; // candles back to compare band width against

    private bool _volatilityOpeningArmed;
    private bool _volatilityOpeningFired;
    private bool _volatilityOpeningBullish; // true = Techo/CALL watch (upper band), false = Piso/PUT watch (lower band)

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
        _volatilityOpeningArmed = true;
        _volatilityOpeningBullish = bullish;

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

    private void EvaluateVolatilityOpening(decimal livePrice)
    {
        if (!_volatilityOpeningArmed || _volatilityOpeningFired) return;

        var current = BollingerBandsAt(_closedCandles.Count - 1);
        var earlier = BollingerBandsAt(_closedCandles.Count - 1 - VolatilityWidthLookback);
        if (current == null || earlier == null) return;

        var currentWidth = current.Value.Upper - current.Value.Lower;
        var earlierWidth = earlier.Value.Upper - earlier.Value.Lower;
        if (currentWidth <= earlierWidth) return; // bands aren't actually widening yet

        if (_volatilityOpeningBullish)
        {
            if (livePrice < current.Value.Upper) return; // hasn't reached the upper band yet
        }
        else
        {
            if (livePrice > current.Value.Lower) return; // hasn't reached the lower band yet
        }

        _volatilityOpeningFired = true;
        var bandLabel = _volatilityOpeningBullish ? "Superior" : "Inferior";
        var bandValue = _volatilityOpeningBullish ? current.Value.Upper : current.Value.Lower;
        var direction = _volatilityOpeningBullish ? "Alza" : "Baja";
        var caption = $"Abriendo la Volatilidad — spot {livePrice:F2} toca Banda {bandLabel} {bandValue:F2}";
        OnVolatilityOpeningEvent?.Invoke(caption);
        _ = SendChartToTelegramAsync(caption);
        EventLogStore.Append(_symbol, "15Min", "VolatilityOpening", direction, caption, livePrice,
            $"BollUpper={current.Value.Upper:F2};BollLower={current.Value.Lower:F2}");
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
                await _webView.CoreWebView2.ExecuteScriptAsync("configureSmas([20,40,100,200]);");
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");

                // T-Line + vertical-arrow persistence (per symbol) — reload whatever was drawn in
                // a previous session so it reappears at the same point, and listen for new/
                // deleted/moved ones from now on so they get saved too.
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

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
            }

            // Bollinger Bands (20, 2 std devs) — 15m RTH panel only (1h gets its own copy above).
            // Also listens for "strike_delete" (see below) — needed here too now that Stk lines
            // can be deleted from ANY of the 3 panels, not just 1h/RTH+Overnight.
            if (_mode == ChartPanelMode.Fifteen_RTH)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }

            // Pre-market blue line (1h and 15m RTH panels): only if the chart is opened before
            // 9:30 AM ET that day — anchors at whatever candle is currently the last one loaded
            // (yesterday's close) and tracks live price until the market opens, then freezes (see
            // Streamer_OnNewCandle). Not persisted; a later re-open restarts the whole thing.
            if (_mode == ChartPanelMode.Fifteen_RTH || _mode == ChartPanelMode.Hourly15)
            {
                var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
                if (nowEastern.TimeOfDay < new TimeSpan(9, 30, 0))
                    await _webView.CoreWebView2.ExecuteScriptAsync("startPreMarketLine();");
            }

            // Gray shading for overnight/weekend gaps — only on the 15m RTH+Overnight panel.
            // Also listens for "dzsz" messages (Demand Zone rebote detection, see
            // EvaluateDemandZoneRebounds) — this panel is the only one with DZ/SZ enabled.
            if (_mode == ChartPanelMode.Fifteen_Full)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureOvernightBands();");
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
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
                            EvaluateCrossings(last);
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
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
        {
            // Pre-market tick on the 1h/15m RTH panels — doesn't form a candle, but feeds the blue
            // pre-market line (if startPreMarketLine was called when this panel opened). Once
            // 9:30 AM ET hits this branch stops firing for that reason, which is what freezes the
            // line in place with no extra "freeze" logic needed.
            if ((_mode == ChartPanelMode.Fifteen_RTH || _mode == ChartPanelMode.Hourly15) && eastern.TimeOfDay < new TimeSpan(9, 30, 0))
            {
                var price = candle.Close;
                BeginInvoke(async () =>
                {
                    if (_webView.CoreWebView2 == null) return;
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        $"updatePreMarketLine({price.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
                });
            }
            return; // outside this panel's session — ignore the tick entirely
        }

        var isHourly = _mode == ChartPanelMode.Hourly15;

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
                    // here just because today's session started; doing so previously fired
                    // Cross-SMA/T-Line events at ~9:31 AM using yesterday's stale close. Only
                    // evaluate when the outgoing bucket is from the SAME trading day as this tick
                    // (HourlyRthBucketKey = dayNumber*100+slot, so dividing by 100 isolates the day).
                    var sameDay = _liveBucketIndex is { } prevIndex && prevIndex / 100 == index / 100;
                    if (sameDay)
                    {
                        EvaluateCrossings(_liveBucket);
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
                    }
                }
                else if (_mode == ChartPanelMode.Fifteen_Full)
                {
                    EvaluateDemandZoneRebounds(_liveBucket);
                    EvaluateSupplyZoneRebounds(_liveBucket);
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
        BeginInvoke(async () => await RunScriptAsync("updateLastCandle", toSend));

        if (_mode == ChartPanelMode.Fifteen_RTH)
            EvaluateVolatilityOpening(candle.Close);
        else if (_mode == ChartPanelMode.Hourly15)
            EvaluatePisoTechoGapLive(candle.Close);
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

        if (_liveBucket == null) return; // no bucket open yet — CHART_EQUITY seeds the first one
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
            return; // outside this panel's session — ignore the tick entirely

        _liveBucket.High  = Math.Max(_liveBucket.High, price);
        _liveBucket.Low   = Math.Min(_liveBucket.Low, price);
        _liveBucket.Close = price;

        var toSend = _liveBucket;
        BeginInvoke(async () => await RunScriptAsync("updateLastCandle", toSend));

        if (_mode == ChartPanelMode.Fifteen_RTH)
            EvaluateVolatilityOpening(price);
        else if (_mode == ChartPanelMode.Hourly15)
            EvaluatePisoTechoGapLive(price);
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
