using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// Standalone window showing a live candlestick chart of the underlying (spot), fed by Schwab's
// streaming WebSocket API — completely separate from the existing polling-based Quotes tab.
public partial class ChartForm : Form
{
    private readonly string _symbol;
    private readonly SchwabStreamerClient _streamer;
    private WebView2 _webView = null!;
    private bool _closing;

    public ChartForm(string symbol, SchwabStreamerClient streamer)
    {
        _symbol   = symbol;
        _streamer = streamer;

        Text          = $"Live Chart — {symbol}";
        Width         = 900;
        Height        = 600;
        StartPosition = FormStartPosition.CenterScreen;

        InitializeWebView();

        _streamer.OnNewCandle    += Streamer_OnNewCandle;
        _streamer.OnDisconnected += Streamer_OnDisconnected;

        FormClosing += ChartForm_FormClosing;
    }

    private void InitializeWebView()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        await _webView.EnsureCoreWebView2Async();

        var chartPath = Path.Combine(AppContext.BaseDirectory, "ChartAssets", "chart.html");
        _webView.CoreWebView2.Navigate(new Uri(chartPath).AbsoluteUri);

        _webView.CoreWebView2.NavigationCompleted += async (s, args) =>
        {
            if (!args.IsSuccess) return;
            await LoadHistoryAndConnectAsync();
        };
    }

    private async Task LoadHistoryAndConnectAsync()
    {
        try
        {
            var history = await _streamer.GetTodaysHistoricalCandlesAsync(_symbol);

            // Only show last Friday's regular session (9:30 AM - 4:00 PM ET), resampled into
            // 1-hour candles — Schwab's pricehistory endpoint doesn't offer a 60-min frequency
            // directly, so we aggregate the 1-minute candles ourselves (7 candles for a 6.5h
            // session: 9:30-10:30 ... 15:30-16:00).
            if (history.Count > 0)
            {
                history = AggregateToHourly(FilterLastFridayRegularSession(history));
                if (history.Count > 0)
                    await RunScriptAsync("cargarHistorial", history);
            }

            await _streamer.ConnectAsync();
            await _streamer.SubscribeChartEquity(_symbol);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the live chart for {_symbol}:\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Keeps only candles from the most recent Friday (relative to now, ET), between 9:30 AM and
    // 4:00 PM ET — the regular session, no pre/after-hours.
    private static List<CandleData> FilterLastFridayRegularSession(List<CandleData> candles)
    {
        var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
        var daysSinceFriday = ((int)nowEastern.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        var lastFriday = nowEastern.Date.AddDays(-daysSinceFriday);
        var sessionStart = lastFriday.AddHours(9).AddMinutes(30);
        var sessionEnd   = lastFriday.AddHours(16);

        return candles
            .Where(c =>
            {
                var eastern = TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone);
                return eastern >= sessionStart && eastern <= sessionEnd;
            })
            .ToList();
    }

    // Groups 1-minute candles into 1-hour buckets starting at 9:30 AM ET (9:30-10:30,
    // 10:30-11:30, ..., 15:30-16:00 — 7 buckets for a 6.5h session). Open = first minute's open,
    // Close = last minute's close, High/Low = extremes across the bucket.
    private static List<CandleData> AggregateToHourly(List<CandleData> minuteCandles)
    {
        if (minuteCandles.Count == 0) return minuteCandles;

        var sessionStartUtc = minuteCandles
            .Select(c => TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone))
            .Min(t => t.Date.AddHours(9).AddMinutes(30));

        return minuteCandles
            .GroupBy(c =>
            {
                var eastern    = TimeZoneInfo.ConvertTimeFromUtc(c.Time, EasternZone);
                var minutesIn  = (eastern - sessionStartUtc).TotalMinutes;
                return (int)Math.Floor(minutesIn / 60.0);
            })
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

    private void Streamer_OnNewCandle(CandleData candle)
    {
        if (_closing || !IsHandleCreated) return;
        Invoke(async () => await RunScriptAsync("actualizarUltimaVela", candle));
    }

    private void Streamer_OnDisconnected(string message)
    {
        if (_closing || !IsHandleCreated) return;
        Invoke(() => Text = $"Live Chart — {_symbol} ({message})");
    }

    // Serializes the payload as JSON and calls the given JS function with it — used for both
    // cargarHistorial(velas[]) and actualizarUltimaVela(vela).
    private async Task RunScriptAsync(string jsFunction, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null // Lightweight Charts expects lowercase "time"/"open"/etc.
        });
        // CandleData's C# PascalCase properties need to map to Lightweight Charts' lowercase
        // fields — remap explicitly rather than relying on serializer naming policy tricks.
        await _webView.CoreWebView2.ExecuteScriptAsync($"{jsFunction}({ToChartJson(payload)});");
    }

    // Lightweight Charts renders the Unix timestamp we give it as literal UTC digits — it does
    // NOT convert to the browser's local timezone. So instead of sending the true UTC instant, we
    // convert to US Eastern wall-clock time first, then lie and mark THAT as UTC — the digits the
    // chart displays then read as New York time, regardless of what timezone the PC is set to.
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

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

    private async void ChartForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _closing = true;
        _streamer.OnNewCandle    -= Streamer_OnNewCandle;
        _streamer.OnDisconnected -= Streamer_OnDisconnected;
        await _streamer.DisposeAsync();
    }
}
