using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
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
// shared SchwabStreamerClient (one connection feeds all 3 panels in MultiChartForm) and
// aggregates the incoming 1-minute candles into its own interval/session on the fly.
public class ChartPanel : Panel
{
    private readonly string _symbol;
    private readonly SchwabStreamerClient _streamer;
    private readonly ChartPanelMode _mode;
    private readonly int _intervalMinutes;
    private readonly bool _rthOnly;
    private readonly Label _header;
    private WebView2 _webView = null!;
    private bool _closing;

    // The bucket currently being built from live 1-min ticks, and which bucket index it belongs
    // to (so we know when a new tick starts a new bucket vs. extends the current one).
    private CandleData? _liveBucket;
    private int? _liveBucketIndex;
    private DateTime _liveAnchor;

    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public ChartPanel(string symbol, SchwabStreamerClient streamer, ChartPanelMode mode)
    {
        _symbol   = symbol;
        _streamer = streamer;
        _mode     = mode;
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

        _streamer.OnNewCandle    += Streamer_OnNewCandle;
        _streamer.OnDisconnected += Streamer_OnDisconnected;

        HandleCreated += async (s, e) => await LoadHistoryAsync();
        Disposed += (s, e) =>
        {
            _closing = true;
            _streamer.OnNewCandle    -= Streamer_OnNewCandle;
            _streamer.OnDisconnected -= Streamer_OnDisconnected;
        };
    }

    private static string ModeLabel(ChartPanelMode mode) => mode switch
    {
        ChartPanelMode.Hourly15     => "1h",
        ChartPanelMode.Fifteen_RTH  => "15m RTH",
        ChartPanelMode.Fifteen_Full => "15m RTH+Overnight",
        _ => mode.ToString()
    };

    private void InitializeWebView()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
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
            _webView.CoreWebView2.Navigate(new Uri(chartPath).AbsoluteUri);
            await navDone.Task;

            // SMA 20/40 overlay — only on the 1h panel for now.
            if (_mode == ChartPanelMode.Hourly15)
                await _webView.CoreWebView2.ExecuteScriptAsync("configurarSMAs([20,40]);");

            // Bollinger Bands (20, 2 std devs) — only on the 15m RTH panel for now.
            if (_mode == ChartPanelMode.Fifteen_RTH)
                await _webView.CoreWebView2.ExecuteScriptAsync("configurarBollinger(20, 2);");

            // Schwab's pricehistory only accepts period = 1,2,3,4,5,10 for periodType=day — 7 is
            // NOT valid (400 Bad Request). Request 10 (the smallest valid value >= 7) for the 1h
            // panel and trim to the last 7 trading days client-side; the 15m panels' period=3 is
            // already a valid value, no trimming needed there.
            var requestDays = _mode == ChartPanelMode.Hourly15 ? 10 : 3;
            var history = await _streamer.GetHistoricalCandlesAsync(_symbol, requestDays);
            if (history.Count > 0)
            {
                var filtered = FilterSession(history, _rthOnly);
                if (_mode == ChartPanelMode.Hourly15)
                    filtered = TrimToLastNDays(filtered, 7);

                var aggregated = AggregateToInterval(filtered, _intervalMinutes, _rthOnly);
                if (aggregated.Count > 0)
                {
                    await RunScriptAsync("cargarHistorial", aggregated);
                    // Seed the live aggregator with the last historical bucket so the first live
                    // tick extends it correctly instead of starting a spurious new one.
                    var last = aggregated[^1];
                    _liveAnchor      = BucketAnchor(new[] { last }, _rthOnly);
                    _liveBucketIndex = BucketIndex(last.Time, _liveAnchor, _intervalMinutes);
                    _liveBucket      = last;
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

    // Keeps only candles from the most recent N distinct trading days (ET calendar date)
    // present in the data — used to trim Schwab's period=10 response down to "last 7 days".
    private static List<CandleData> TrimToLastNDays(List<CandleData> candles, int days)
    {
        var keepDates = candles
            .Select(c => TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone).Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(days)
            .ToHashSet();

        return candles
            .Where(c => keepDates.Contains(TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone).Date))
            .ToList();
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
    private void Streamer_OnNewCandle(CandleData candle)
    {
        if (_closing || !IsHandleCreated) return;

        var eastern = TimeZoneInfo.ConvertTimeFromUtc(candle.Time, EasternZone);
        if (_rthOnly && (eastern.TimeOfDay < new TimeSpan(9, 30, 0) || eastern.TimeOfDay > new TimeSpan(16, 0, 0)))
            return; // outside this panel's session — ignore the tick entirely

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
                // New bucket — start fresh (the previous bucket stays on the chart as-is; its
                // last update already reflects its final close).
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

    private void Streamer_OnDisconnected(string message)
    {
        if (_closing || !IsHandleCreated) return;
        BeginInvoke(() => _header.Text = $"{_symbol} — {ModeLabel(_mode)} — {message}");
    }

    // Serializes the payload as JSON and calls the given JS function with it — used for both
    // cargarHistorial(velas[]) and actualizarUltimaVela(vela).
    private async Task RunScriptAsync(string jsFunction, object payload)
    {
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
            var easternWallClock = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.Time, DateTimeKind.Utc), EasternZone);
            var fakeUtcForDisplay = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Utc);
            return new
            {
                time  = new DateTimeOffset(fakeUtcForDisplay).ToUnixTimeSeconds(),
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
}
