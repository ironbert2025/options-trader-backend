using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Minimal, standalone chart panel for SimulatorForm — reuses the SAME chart.html/Lightweight
// Charts asset as the live ChartPanel, but has NO streaming connection and NO REST history fetch.
// It only ever shows whatever candle list it's told to via CargarHastaPasoAsync — SimulatorForm
// recomputes and pushes that list on every step.
//
// Deliberately NOT a subclass or variant of ChartPanel — completely separate so nothing here can
// ever affect the live chart's behavior, even by accident. Cross-SMA and T-Line+SMA20 signal
// detection (Hourly15 only) are ported copies of ChartPanel's own logic, evaluated against
// whichever hourly candle just became "closed" as the simulator steps forward — no Telegram, no
// disk persistence (not even the T-Line itself), matching this being a practice-only sandbox.
public class SimulatedChartPanel : Panel
{
    private readonly Label _header;
    private readonly ChartPanelMode _mode;
    private WebView2 _webView = null!;
    private TaskCompletionSource? _readyTcs;

    public SimulatedChartPanel(string title, ChartPanelMode mode)
    {
        _mode = mode;
        _header = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(19, 23, 34),
            Text      = title
        };

        _webView = new WebView2 { Dock = DockStyle.Fill };

        Controls.Add(_webView);
        Controls.Add(_header);

        HandleCreated += async (s, e) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _readyTcs = new TaskCompletionSource();
        try
        {
            await _webView.EnsureCoreWebView2Async();

            var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
            var navDone = new TaskCompletionSource();
            _webView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (args.IsSuccess) navDone.TrySetResult();
            };

            // Same cache-busting pattern as ChartPanel — avoids WebView2 serving a stale cached
            // copy of chart.html across window instances within the same process.
            var chartUri = new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}";
            _webView.CoreWebView2.Navigate(chartUri);
            await navDone.Task;

            // Same SMA/Bollinger overlays as the live ChartPanel.LoadHistoryAsync, per mode —
            // 1h gets SMA 20/40/100/200 + Bollinger, 15m RTH gets Bollinger only, RTH+Overnight
            // gets neither (matches the live chart exactly).
            if (_mode == ChartPanelMode.Hourly15)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureSmas([20,40,100,200]);");
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }
            else if (_mode == ChartPanelMode.Fifteen_RTH)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("configureBollinger(20, 2);");
            }
            else if (_mode == ChartPanelMode.Fifteen_Full)
            {
                // Gray shading for overnight/weekend gaps — same as ChartPanel.LoadHistoryAsync's
                // "only on the 15m RTH+Overnight panel" call. recalculateOvernightBands() re-runs
                // automatically on every loadHistory() (chart.html), so it stays in sync as the
                // simulator steps forward — no extra wiring needed here beyond enabling it once.
                await _webView.CoreWebView2.ExecuteScriptAsync("configureOvernightBands();");

                // Needed for "dzsz" messages (DZ/SZ mirroring onto the RTH chart, and Demand Zone
                // rebote tracking below) — this is the only chart with DZ/SZ armed.
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }

            _readyTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
        }
    }

    private bool _visibleDaysSet;

    // Must be called before CargarHastaPasoAsync whenever the user loads a DIFFERENT simulation
    // day (SimulatorForm.LoadSelectedDay) — this WebView instance is reused across day loads
    // (unlike the live chart, created once per session), so without this the previous day's
    // pan/zoom state would stick and get reapplied to the new day's candles, visually misplacing
    // them (e.g. the new day's 9:30 candle landing wherever the old view's edge used to be).
    // Stepping ◀/▶ within the same day must NOT call this — that's what preserves pan/zoom there.
    public async Task ResetViewForNewDayAsync()
    {
        if (_readyTcs != null) await _readyTcs.Task;
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("resetViewForNewDay();");
        _visibleDaysSet = false;
    }

    // Replaces the whole visible candle series with the given list — used instead of the live
    // ChartPanel's incremental "extend current bucket" logic, since a step-through simulator can
    // simply recompute "everything up to the current step" on every ◀/▶ click; no need to
    // replicate ChartPanel's live-bucket state machine here.
    //
    // visibleDays matches the live chart's own default zoom (7 for the 1h panel, 3 for the 15m
    // ones — see ChartPanel.LoadHistoryAsync) so the simulator reads the same as a real chart.
    public async Task CargarHastaPasoAsync(List<CandleData> candles, int visibleDays)
    {
        if (_readyTcs != null) await _readyTcs.Task;
        if (_webView.CoreWebView2 == null || candles.Count == 0) return;

        if (!_visibleDaysSet)
        {
            await _webView.CoreWebView2.ExecuteScriptAsync($"configureVisibleDays({visibleDays});");
            _visibleDaysSet = true;
        }
        await RunScriptAsync("loadHistory", candles);

        if (_mode == ChartPanelMode.Hourly15 || _mode == ChartPanelMode.Fifteen_Full)
            EvaluateNewlyClosedCandles(candles);
    }

    // Same green "Stk=xxx" line as the live ChartPanel.MarkStrikeAsync — fired when a demo trade
    // opens in the Simulator. Accumulates, never auto-removed.
    public async Task MarkStrikeAsync(decimal strike)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"markStrike({priceStr});");
    }

    private async Task RunScriptAsync(string jsFunction, List<CandleData> candles)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"{jsFunction}({ToChartJson(candles)});");
    }

    // Same "ET wall-clock digits disguised as UTC" trick ChartPanel uses (see its
    // FakeUtcEpochSeconds) — Lightweight Charts renders the Unix timestamp as literal UTC digits.
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // Public so SimulatorForm can compute the same fake-epoch value for a candle's real UTC time
    // when it needs to compare against a DZ/SZ line's time (see AddMirroredZoneLineAsync callers).
    public static long ToFakeUtcEpochSeconds(DateTime utcTime)
    {
        var easternWallClock = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), EasternZone);
        var fakeUtcForDisplay = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Utc);
        return new DateTimeOffset(fakeUtcForDisplay).ToUnixTimeSeconds();
    }

    private static string ToChartJson(List<CandleData> candles)
    {
        object Map(CandleData c) => new
        {
            time  = ToFakeUtcEpochSeconds(c.Time),
            open  = c.Open,
            high  = c.High,
            low   = c.Low,
            close = c.Close
        };

        return System.Text.Json.JsonSerializer.Serialize(candles.Select(Map));
    }

    // ==================================================================================
    // Cross-SMA (Cruce/Rebote) — ported from ChartPanel, log-only (no Telegram, no persistence).
    // ==================================================================================

    private readonly List<CandleData> _closedCandles = new();
    private readonly SortedSet<int> _crossArmedPeriods = new();
    private int? _crossActivePeriod;
    private bool _crossUp;
    private bool _crossFinished;

    private static readonly Dictionary<int, string> SmaColorNames = new()
    {
        [20] = "Yellow", [40] = "Red", [100] = "Green", [200] = "Purple"
    };

    // ==================================================================================
    // Piso/Techo auto-analysis — ported from ChartPanel. Unlike ChartPanel's version (static,
    // computed once per app instance/process), here it's an INSTANCE field recomputed once per
    // simulated DAY LOAD (called from SimulatorForm.LoadSelectedDay, same "once per day, not per
    // step" precedent as EvaluateDailyBounce) — a fresh simulated day is the closest equivalent to
    // "a new pre-market session" here, there's no real wall-clock "once per process" to anchor to.
    // ==================================================================================

    private sealed class PisoTechoWatch
    {
        public int Period;
        public bool WatchingUp; // true = Techo (expects reject down / cross up), false = Piso (expects bounce up / cross down)
        public bool Done;
    }
    private readonly List<PisoTechoWatch> _pisoTechoWatches = new();

    // Set by SimulatorForm on every LoadSelectedDay — Cruce/Rebote only fires for candles ON OR
    // AFTER this date. Without it, EvaluatePisoTechoWatches would evaluate the ENTIRE prior-
    // context backlog (now up to ~200 trading days, since HourlyCandleStore's cap grew) as
    // "already closed" the instant the day loads, firing instantly against some ancient candle
    // instead of waiting for the actual replayed-day candle that closes past the SMA.
    public DateOnly? WatchStartDate { get; set; }

    // Fires (caption, price, eventType, direction, reference) once per resolved Cruce/Rebote —
    // log-only for the simulator (no Telegram), but SimulatorForm's handler also persists it to
    // events_log.csv, same as Demand Zone, per explicit request that this one gets written to disk.
    public event Action<string, decimal, string, string, string>? OnPisoTechoOutcomeEvent;

    // Called once per day load with the SMA-pair results already computed by SimulatorForm
    // (mirrors ChartPanel.EvaluatePisoTechoPair, computed there against _hourlyCandles before
    // _simDate). Draws both labels and arms both periods of each non-null pair independently.
    public async Task SetPisoTechoResultsAsync(string? result2040, string? result100200)
    {
        if (_readyTcs != null) await _readyTcs.Task;

        _pisoTechoWatches.Clear();
        ArmPisoTechoWatch(20, 40, result2040);
        ArmPisoTechoWatch(100, 200, result100200);

        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"markPisoTecho(20, 40, {ToJsStringOrNull(result2040)});");
        await _webView.CoreWebView2.ExecuteScriptAsync($"markPisoTecho(100, 200, {ToJsStringOrNull(result100200)});");
    }

    private void ArmPisoTechoWatch(int fastPeriod, int slowPeriod, string? result)
    {
        if (result == null) return;
        var watchingUp = result == "Techo";
        _pisoTechoWatches.Add(new PisoTechoWatch { Period = fastPeriod, WatchingUp = watchingUp });
        _pisoTechoWatches.Add(new PisoTechoWatch { Period = slowPeriod, WatchingUp = watchingUp });
    }

    private static string ToJsStringOrNull(string? value) => value == null ? "null" : $"'{value}'";

    // Same case-1/case-2 cross-or-bounce formula as EvaluateCrossings, evaluated per watched
    // period independently. Resolves once, then stops for the rest of the simulated day.
    private void EvaluatePisoTechoWatches(CandleData justClosed)
    {
        if (WatchStartDate is { } startDate &&
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(justClosed.Time, EasternZone)) < startDate)
            return; // prior-context candle (backfilled history), not part of the replayed day

        foreach (var watch in _pisoTechoWatches)
        {
            if (watch.Done) continue;

            var currentSma = Sma(watch.Period, _closedCandles.Count - 1);
            if (currentSma == null) continue;

            var isGreen = justClosed.Close > justClosed.Open;
            var isRed   = justClosed.Close < justClosed.Open;

            var crossed = watch.WatchingUp
                ? isGreen && justClosed.Close > currentSma && justClosed.Open <= currentSma
                : isRed   && justClosed.Close < currentSma && justClosed.Open >= currentSma;

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
            var caption   = $"{evento} en {pisoTecho} — SMA{watch.Period} — cierre {justClosed.Close:F2} (SMA{watch.Period} {currentSma.Value:F2})";
            OnPisoTechoOutcomeEvent?.Invoke(caption, justClosed.Close, $"PisoTecho{evento}", pisoTecho, $"SMA{watch.Period}={currentSma.Value:F2}");
        }
    }

    public event Action? OnCrossSequenceFinished;
    public event Action<string>? OnCrossSequenceEvent;
    public event Action<string>? OnTLineSignalEvent;

    public (bool Armed, bool Up) ToggleCrossMonitor(int period)
    {
        if (_crossArmedPeriods.Remove(period))
        {
            if (_crossActivePeriod == period) AdvanceCrossSequence(period);
            return (false, false);
        }

        if (_crossFinished) return (false, false);

        if (_crossActivePeriod == null)
        {
            var currentPrice = _closedCandles.LastOrDefault()?.Close;
            var currentSma   = _closedCandles.Count > 0 ? Sma(period, _closedCandles.Count - 1) : null;
            if (currentPrice == null || currentSma == null) return (false, false);

            _crossUp = currentPrice < currentSma;
            _crossActivePeriod = period;
        }

        _crossArmedPeriods.Add(period);
        return (true, _crossUp);
    }

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

    private const decimal BounceProximityRatio = 0.30m;

    private void EvaluateCrossings(CandleData justClosed)
    {
        if (_crossFinished || _crossActivePeriod == null) return;

        var period = _crossActivePeriod.Value;
        if (_closedCandles.Count < period + 1) return;

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
            FireCrossSequenceEvent(period, "Cruce");
            AdvanceCrossSequence(period);
            return;
        }

        var bounced = _crossUp
            ? justClosed.Open < currentSma && isRed &&
                (justClosed.High > currentSma
                    ? justClosed.Close < currentSma
                    : (currentSma - justClosed.High) < BounceProximityRatio * (justClosed.High - justClosed.Close))
            : justClosed.Open > currentSma && isGreen &&
                (justClosed.Low < currentSma
                    ? justClosed.Close > currentSma
                    : (justClosed.Low - currentSma) < BounceProximityRatio * (justClosed.Close - justClosed.Low));

        if (bounced) FireCrossSequenceEvent(period, "Rebote");
    }

    private void FireCrossSequenceEvent(int period, string eventLabel)
    {
        var direction = _crossUp ? "UP" : "DOWN";
        var colorName = SmaColorNames.TryGetValue(period, out var c) ? c : string.Empty;
        OnCrossSequenceEvent?.Invoke($"{eventLabel} {direction} SMA {period}({colorName})");
    }

    private decimal? Sma(int period, int endIndex)
    {
        if (endIndex < period - 1 || endIndex >= _closedCandles.Count) return null;
        decimal sum = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++) sum += _closedCandles[i].Close;
        return sum / period;
    }

    // ==================================================================================
    // T-Line + SMA20 breakout — ported from ChartPanel. Only 1 T-Line, in memory only (no
    // TLineStore — nothing about a practice T-Line should survive closing the simulator).
    // ==================================================================================

    private const int TLineSmaPeriod = 20;
    private (long T1, decimal P1, long T2, decimal P2)? _tLine;
    private bool _tLineSignalFired;
    private bool _tLineArmed;

    public async Task<bool> ToggleTLineModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleTLine();");
        _tLineArmed = result == "true";
        return _tLineArmed;
    }

    public async Task ClearTLineAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("clearDrawings();");
        _tLine = null;
        _tLineSignalFired = false;
    }

    // ==================================================================================
    // DZ/SZ (Demand Zone / Supply Zone) — ported toggle from ChartPanel, plus a mirroring hook
    // (SimulatorForm listens for OnDzSzLineDrawn on the RTH+Overnight chart and forwards each
    // line to the 15m RTH chart's AddMirroredZoneLineAsync, at the same price).
    // ==================================================================================

    // Fires (fakeUtcTime, price, color) every time a DZ/SZ line is drawn on THIS chart.
    public event Action<long, decimal, string>? OnDzSzLineDrawn;

    public async Task<bool> ToggleDzSzModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleDzSz();");
        return result == "true";
    }

    // Clears everything drawn on THIS chart (same as ClearTLineAsync — chart.html's
    // clearDrawings() is shared/global per WebView instance).
    public async Task ClearDzSzAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("clearDrawings();");
        _dzSzPendingPrices.Clear();
        _demandZones.Clear();
    }

    // ---- Demand Zone rebote — ported from ChartPanel.EvaluateDemandZoneRebounds. Log-only (no
    // Telegram, no EventLogStore call here — SimulatorForm's handler does that, since it's the
    // one that knows the current symbol; see the OnDemandZoneReboundEvent subscription). ----
    private readonly List<decimal> _dzSzPendingPrices = new();
    private readonly List<DemandZoneState> _demandZones = new();

    private sealed class DemandZoneState
    {
        public decimal Proximal; // green line — upper boundary
        public decimal Distal;   // red line — lower boundary
        public bool Entered;
        public bool Done;
    }

    // Fires (caption, price, proximal, distal) once per confirmed rebote.
    public event Action<string, decimal, decimal, decimal>? OnDemandZoneReboundEvent;

    // Same case-1/case-2 proximity idea as EvaluateCrossings' bounce detection — a candle whose
    // Low falls short of Proximal but within BounceProximityRatio of the rejection move's size
    // (Close - Low) still counts as touching it. See ChartPanel's identical copy for the full
    // rationale.
    private void EvaluateDemandZoneRebounds(CandleData justClosed)
    {
        foreach (var zone in _demandZones)
        {
            if (zone.Done) continue;

            if (!zone.Entered)
            {
                var touchedOrClose = justClosed.Low <= zone.Proximal ||
                    (justClosed.Low - zone.Proximal) < BounceProximityRatio * (justClosed.Close - justClosed.Low);
                if (!touchedOrClose) continue;
                zone.Entered = true;
            }

            if (justClosed.Low < zone.Distal)
            {
                zone.Done = true;
                continue;
            }

            if (justClosed.Close > zone.Proximal)
            {
                zone.Done = true;
                var caption = $"Rebote en Zona de Demanda — cierre {justClosed.Close:F2} (Proximal {zone.Proximal:F2}, Distal {zone.Distal:F2})";
                OnDemandZoneReboundEvent?.Invoke(caption, justClosed.Close, zone.Proximal, zone.Distal);
            }
        }
    }

    // Adds a DZ/SZ line to THIS chart without arming click mode — used to mirror a line drawn on
    // another chart. fakeUtcTime is the same "ET wall-clock digits disguised as UTC" epoch value
    // ToFakeUtcEpochSeconds/chart.html candles already use, so it can be forwarded verbatim from
    // one chart's OnDzSzLineDrawn straight into another's AddMirroredZoneLineAsync.
    public async Task AddMirroredZoneLineAsync(long fakeUtcTime, decimal price, string color)
    {
        if (_webView.CoreWebView2 == null) return;
        var priceStr = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _webView.CoreWebView2.ExecuteScriptAsync($"addMirroredZoneLine({fakeUtcTime}, {priceStr}, '{color}');");
    }

    public async Task ClearMirroredZoneLinesAsync()
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("clearMirroredZoneLines();");
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (type == "dzsz")
            {
                var dzTime  = root.GetProperty("time").GetInt64();
                var dzPrice = root.GetProperty("price").GetDecimal();
                var dzColor = root.GetProperty("color").GetString() ?? "#26a69a";
                OnDzSzLineDrawn?.Invoke(dzTime, dzPrice, dzColor);

                // Every 2 lines form a pair — only a genuine demand zone (green/"demand" line
                // above red/"supply") gets tracked for rebote detection (same geometry chart.html's
                // own fill uses). Only relevant on this chart (RTH+Overnight — the only one with
                // DZ/SZ armed), harmless no-op on the mirror-only RTH chart since nothing evaluates it there.
                _dzSzPendingPrices.Add(dzPrice);
                if (_dzSzPendingPrices.Count == 2)
                {
                    var (demandPrice, supplyPrice) = (_dzSzPendingPrices[0], _dzSzPendingPrices[1]);
                    _dzSzPendingPrices.Clear();
                    if (demandPrice > supplyPrice)
                        _demandZones.Add(new DemandZoneState { Proximal = demandPrice, Distal = supplyPrice });
                }
                return;
            }

            if (type != "tline" && type != "tline_delete") return;

            var t1 = root.GetProperty("t1").GetInt64();
            var p1 = root.GetProperty("p1").GetDecimal();
            var t2 = root.GetProperty("t2").GetInt64();
            var p2 = root.GetProperty("p2").GetDecimal();

            if (type == "tline")
            {
                if (_tLine != null)
                {
                    _ = _webView.CoreWebView2?.ExecuteScriptAsync("removeLastTLine();");
                    MessageBox.Show(
                        "Ya existe una T-Line dibujada. Borrala (Clear) antes de dibujar una nueva.",
                        "T-Line ya existe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _tLine = (t1, p1, t2, p2);
                _tLineSignalFired = false;
            }
            else
            {
                _tLine = null;
                _tLineSignalFired = false;
            }
        }
        catch
        {
            // Malformed/unexpected message — ignore, not fatal.
        }
    }

    private void EvaluateTLineSignal(CandleData justClosed)
    {
        if (_tLineSignalFired || _tLine == null) return;

        var (t1, p1, t2, p2) = _tLine.Value;
        var candleTimeSec = new DateTimeOffset(DateTime.SpecifyKind(justClosed.Time, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var tLineValue = TLineValueAt(t1, p1, t2, p2, candleTimeSec);

        if (_closedCandles.Count < TLineSmaPeriod) return;
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
        OnTLineSignalEvent?.Invoke($"CT {direction} en Hora — cierre {justClosed.Close:F2} (T-Line {tLineValue:F2}, SMA{TLineSmaPeriod} {sma20.Value:F2})");
    }

    private static decimal TLineValueAt(long t1, decimal p1, long t2, decimal p2, long atTime)
    {
        if (t2 == t1) return p1;
        var slope = (p2 - p1) / (t2 - t1);
        return p1 + slope * (atTime - t1);
    }

    // ==================================================================================
    // Bridges the simulator's "resend the whole candle list every step" model into the live
    // chart's "evaluate once when a candle closes" model — a candle is treated as closed once a
    // NEWER one appears after it in the list (the last candle in the list is always assumed
    // still-forming, same as the live chart's _liveBucket). Handles jumps of more than 1 candle
    // (e.g. the "Ir a hora" buttons) by evaluating every newly-closed candle in order, not just
    // the latest one, so the Cross-SMA sequence never skips a step.
    // ==================================================================================
    private void EvaluateNewlyClosedCandles(List<CandleData> candles)
    {
        var closedNow = candles.Count > 0 ? candles.Take(candles.Count - 1).ToList() : new List<CandleData>();

        // A step going backwards (◀) or a jump to an earlier time must roll the sequence state
        // back too, or re-evaluating from scratch would replay already-fired events.
        if (closedNow.Count < _closedCandles.Count)
        {
            _closedCandles.Clear();
            _crossArmedPeriods.Clear();
            _crossActivePeriod = null;
            _crossFinished = false;
            _tLineSignalFired = false;
            foreach (var zone in _demandZones) { zone.Entered = false; zone.Done = false; }
            foreach (var watch in _pisoTechoWatches) watch.Done = false;
        }

        for (int i = _closedCandles.Count; i < closedNow.Count; i++)
        {
            _closedCandles.Add(closedNow[i]);
            EvaluateCrossings(closedNow[i]);
            EvaluateTLineSignal(closedNow[i]);
            EvaluateDemandZoneRebounds(closedNow[i]);
            EvaluatePisoTechoWatches(closedNow[i]);
        }
    }
}
