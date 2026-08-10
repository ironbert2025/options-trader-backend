using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Plain read-only candlestick viewer for ONE (symbol, interval) pair — used by
// TimeframeViewerForm to show the same symbol at several timeframes side by side. Deliberately a
// separate, much smaller class from ChartPanel: no drawing tools beyond DZ/SZ, no SMA/Bollinger/
// Piso-Techo detection, no Telegram pushes of its own — just candles + live updates + the gray
// overnight shading. The one exception is Demand/Supply Zone REBOTE detection (enableZoneRebounds),
// which mirrors ChartPanel's exact logic but only fires OnZoneReboundEvent — the actual Telegram
// push (combined 4-chart snapshot) is built by TimeframeViewerForm, which owns all 4 panels.
//
// Always RTH+Overnight (regular session + pre/after-hours, whatever Schwab returns) per explicit
// request, regardless of intervalMinutes — same session window as ChartPanel's Fifteen_Full mode.
public class TimeframeChartPanel : Panel
{
    private readonly string _symbol;
    private readonly string _timeframeLabel;
    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly int _intervalMinutes;
    private readonly int _requestDays;
    private readonly bool _enableZoneRebounds;
    private readonly Label _header;
    private WebView2 _webView = null!;
    private bool _closing;

    private CandleData? _liveBucket;
    private long? _liveBucketIndex;
    private DateTime _liveAnchor;

    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    // ---- Demand/Supply Zone rebote — same convention/logic as ChartPanel (see there for the full
    // rationale): 1st click of a pair is always green/Proximal, 2nd is always red/Distal; which
    // one ends up numerically higher tells demand from supply apart. Only tracked/evaluated when
    // _enableZoneRebounds is true (5m/15m panels — see TimeframeViewerForm). ----
    private readonly List<decimal> _dzSzPendingPrices = new();
    private readonly List<DemandZoneState> _demandZones = new();
    private readonly List<SupplyZoneState> _supplyZones = new();
    private const decimal BounceProximityRatio = 0.30m;

    private sealed class DemandZoneState
    {
        public decimal Proximal;
        public decimal Distal;
        public bool Entered;
        public bool Done;
    }

    private sealed class SupplyZoneState
    {
        public decimal Proximal;
        public decimal Distal;
        public bool Entered;
        public bool Done;
    }

    // Fires (caption, direction, price) when a Demand/Supply Zone rebote confirms — only for
    // panels with enableZoneRebounds. TimeframeViewerForm listens to build+send the Telegram push.
    // Includes the panel itself (always `this`) so the form knows exactly which of the 4 charts
    // to draw the 8 OTM strike lines on, without needing a separate lookup.
    public event Action<TimeframeChartPanel, string, string, decimal>? OnZoneReboundEvent;

    // Fires the raw live price on EVERY tick (not just closed candles) — used by
    // TimeframeViewerForm to detect the SpotPrice cross for the second push.
    public event Action<decimal>? OnLiveTick;

    public TimeframeChartPanel(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, int intervalMinutes, string label, bool enableZoneRebounds = false)
    {
        _symbol             = symbol;
        _timeframeLabel     = label;
        _historyClient      = historyClient;
        _liveFeed           = liveFeed;
        _intervalMinutes    = intervalMinutes;
        _enableZoneRebounds = enableZoneRebounds;
        // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day — 10 gives
        // the most context for the coarser timeframes (1h/4h), 3 is plenty for 5m/15m (a 10-day
        // fetch at 1-minute resolution would just be more data than a 5-15m chart needs to show).
        _requestDays = intervalMinutes >= 60 ? 10 : 3;

        _header = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(19, 23, 34),
            Text      = $"{symbol} — {label}"
        };

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
        Controls.Add(_header);

        _liveFeed.OnNewCandle += Streamer_OnNewCandle;

        HandleCreated += async (s, e) => await LoadHistoryAsync();
        Disposed += (s, e) =>
        {
            _closing = true;
            _liveFeed.OnNewCandle -= Streamer_OnNewCandle;
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        };
    }

    // Renders this panel's actual chart content via the WebView2 engine itself (not a screen
    // capture) — used by TimeframeViewerForm to build the combined 4-chart Telegram snapshot.
    public async Task<Bitmap> CaptureImageAsync()
    {
        using var stream = new MemoryStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

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

            // Same cache-busting query string as ChartPanel — forces a fresh read after rebuilds.
            var chartUri = new Uri(chartPath).AbsoluteUri + $"?v={File.GetLastWriteTimeUtc(chartPath).Ticks}";
            _webView.CoreWebView2.Navigate(chartUri);
            await navDone.Task;

            // Gray shading for overnight/weekend gaps — same as ChartPanel's Fifteen_Full panel.
            await _webView.CoreWebView2.ExecuteScriptAsync("configureOvernightBands();");
            await _webView.CoreWebView2.ExecuteScriptAsync("configureVisibleDays(3);");

            // Only listens for the "dzsz" message (zone-pair classification) — needed to track
            // zones for rebote detection. Panels without enableZoneRebounds never wire this, so
            // drawing zones there stays purely visual with zero C# involvement.
            if (_enableZoneRebounds)
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var history = await _historyClient.GetHistoricalCandlesAsync(_symbol, _requestDays);
            if (history.Count == 0) return;

            var aggregated = CandleAggregation.AggregateToInterval(history, _intervalMinutes, rthOnly: false);
            if (aggregated.Count == 0) return;

            await RunScriptAsync("loadHistory", aggregated);

            // Seed the live aggregator with the last historical bucket so the first live tick
            // extends it correctly instead of starting a spurious new one.
            var last = aggregated[^1];
            _liveAnchor      = TimeZoneInfo.ConvertTimeFromUtc(last.Time, EasternZone).Date;
            _liveBucketIndex = CandleAggregation.BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
            _liveBucket      = last;
        }
        catch (Exception ex)
        {
            if (_closing) return;
            _header.Text = $"{_symbol} — error: {ex.Message}";
        }
    }

    // Toggles DZ/SZ (Demand/Supply Zone) drawing mode on/off. Purely visual when
    // enableZoneRebounds is false; on the 5m/15m panels the drawn zones also get tracked for
    // rebote detection (see EvaluateDemandZoneRebounds/EvaluateSupplyZoneRebounds below).
    public async Task<bool> ToggleDzSzModeAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("toggleDzSz();");
        return result == "true";
    }

    // Green Stk line + "Stk=xxx   Ask=xxx" label — used by TimeframeViewerForm to draw the 5
    // nearest OTM strikes on this panel right before it captures the combined Telegram snapshot,
    // once a Demand/Supply zone rebote confirms. Same primitive/rendering as ChartPanel's
    // markStrike (green line, selectable, deletable with Delete), just with a custom label.
    public async Task MarkStrikeWithAskAsync(decimal strike, decimal ask)
    {
        if (_webView.CoreWebView2 == null) return;
        var strikeStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var askStr    = ask.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var label     = JsonSerializer.Serialize($"Stk={strikeStr}   Ask={askStr}");
        await _webView.CoreWebView2.ExecuteScriptAsync($"markStrike({strikeStr}, {label});");
    }

    // Appends "   Bid=xxx" to an existing Stk line's label in place — used once the SpotPrice
    // cross confirms (see TimeframeViewerForm), doesn't touch the line's position/selection.
    public async Task AppendStrikeLabelAsync(decimal strike, string extraText)
    {
        if (_webView.CoreWebView2 == null) return;
        var strikeStr = strike.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var extraJson = JsonSerializer.Serialize(extraText);
        await _webView.CoreWebView2.ExecuteScriptAsync($"appendStrikeLineLabel({strikeStr}, {extraJson});");
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "dzsz")
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
            }
        }
        catch
        {
            // Malformed/unexpected message from the page — ignore, not fatal.
        }
    }

    // Exact mirror of ChartPanel.EvaluateDemandZoneRebounds — see there for the full case-1/case-2
    // Entrada/Rota/Rebote-confirmado rationale. Only the push mechanism differs: this fires
    // OnZoneReboundEvent instead of sending its own screenshot, since TimeframeViewerForm needs to
    // build the combined 4-chart image (this panel alone can't).
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
                zone.Done = true; // broken
                continue;
            }

            if (justClosed.Close > zone.Proximal)
            {
                zone.Done = true;
                var caption = $"Rebote en Zona de Demanda ({_timeframeLabel}) — cierre {justClosed.Close:F2} (Proximal {zone.Proximal:F2}, Distal {zone.Distal:F2})";
                EventLogStore.Append(_symbol, _timeframeLabel, "DemandZoneRebound", "Alza", caption, justClosed.Close,
                    $"Proximal={zone.Proximal:F2};Distal={zone.Distal:F2}");
                OnZoneReboundEvent?.Invoke(this, caption, "Alza", justClosed.Close);
            }
        }
    }

    // Symmetric counterpart — exact mirror of ChartPanel.EvaluateSupplyZoneRebounds.
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
                var caption = $"Rebote en Zona de Supply ({_timeframeLabel}) — cierre {justClosed.Close:F2} (Proximal {zone.Proximal:F2}, Distal {zone.Distal:F2})";
                EventLogStore.Append(_symbol, _timeframeLabel, "SupplyZoneRebound", "Baja", caption, justClosed.Close,
                    $"Proximal={zone.Proximal:F2};Distal={zone.Distal:F2}");
                OnZoneReboundEvent?.Invoke(this, caption, "Baja", justClosed.Close);
            }
        }
    }

    private void Streamer_OnNewCandle(string symbol, CandleData candle)
    {
        if (symbol != _symbol) return; // one shared connection carries all tickers — ignore others
        if (_closing || !IsHandleCreated) return;

        OnLiveTick?.Invoke(candle.Close);

        if (_liveBucket == null)
        {
            _liveAnchor      = TimeZoneInfo.ConvertTimeFromUtc(candle.Time, EasternZone).Date;
            _liveBucketIndex = CandleAggregation.BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
            _liveBucket      = new CandleData { Time = candle.Time, Open = candle.Open, High = candle.High, Low = candle.Low, Close = candle.Close };
        }
        else
        {
            var index = CandleAggregation.BucketIndex(candle.Time, _liveAnchor, _intervalMinutes);
            if (index != _liveBucketIndex)
            {
                if (_enableZoneRebounds)
                {
                    EvaluateDemandZoneRebounds(_liveBucket);
                    EvaluateSupplyZoneRebounds(_liveBucket);
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

        // series.update() (what updateLastCandle calls in chart.html) updates the last bar if the
        // time matches, or appends a new one if it's later — so the same call covers both "extend
        // the forming bucket" and "a new bucket just started", no separate JS path needed.
        var toSend = _liveBucket;
        BeginInvoke(async () => await RunScriptAsync("updateLastCandle", toSend));
    }

    private async Task RunScriptAsync(string jsFunction, object payload)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"{jsFunction}({ToChartJson(payload)});");
    }

    // Same "ET wall-clock digits disguised as UTC" trick as ChartPanel.ToChartJson/FakeUtcEpochSeconds.
    private static string ToChartJson(object payload)
    {
        static object Map(CandleData c) => new
        {
            time  = ChartPanel.FakeUtcEpochSeconds(c.Time),
            open  = c.Open,
            high  = c.High,
            low   = c.Low,
            close = c.Close
        };

        return payload switch
        {
            CandleData single => JsonSerializer.Serialize(Map(single)),
            List<CandleData> many => JsonSerializer.Serialize(many.Select(Map)),
            _ => "null"
        };
    }
}
