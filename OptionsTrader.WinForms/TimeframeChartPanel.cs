using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Plain read-only candlestick viewer for ONE (symbol, interval) pair — used by
// TimeframeViewerForm to show the same symbol at several timeframes side by side. Deliberately a
// separate, much smaller class from ChartPanel: no drawing tools, no SMA/Bollinger/Piso-Techo/
// DZ-SZ detection, no Telegram pushes — just candles + live updates + the gray overnight shading,
// so opening this viewer never triggers any of ChartPanel's auto-detection/push side effects.
//
// Always RTH+Overnight (regular session + pre/after-hours, whatever Schwab returns) per explicit
// request, regardless of intervalMinutes — same session window as ChartPanel's Fifteen_Full mode.
public class TimeframeChartPanel : Panel
{
    private readonly string _symbol;
    private readonly SchwabStreamerClient _historyClient;
    private readonly ICandleFeed _liveFeed;
    private readonly int _intervalMinutes;
    private readonly int _requestDays;
    private readonly Label _header;
    private WebView2 _webView = null!;
    private bool _closing;

    private CandleData? _liveBucket;
    private long? _liveBucketIndex;
    private DateTime _liveAnchor;

    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public TimeframeChartPanel(string symbol, SchwabStreamerClient historyClient, ICandleFeed liveFeed, int intervalMinutes, string label)
    {
        _symbol          = symbol;
        _historyClient   = historyClient;
        _liveFeed        = liveFeed;
        _intervalMinutes = intervalMinutes;
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
        };
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

    private void Streamer_OnNewCandle(string symbol, CandleData candle)
    {
        if (symbol != _symbol) return; // one shared connection carries all tickers — ignore others
        if (_closing || !IsHandleCreated) return;

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
