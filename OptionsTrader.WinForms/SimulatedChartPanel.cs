using Microsoft.Web.WebView2.WinForms;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Minimal, standalone chart panel for SimulatorForm — reuses the SAME chart.html/Lightweight
// Charts asset as the live ChartPanel, but has NO streaming connection, NO REST history fetch,
// and NO drawing tools. It only ever shows whatever candle list it's told to via
// CargarHastaPasoAsync — SimulatorForm recomputes and pushes that list on every step.
//
// Deliberately NOT a subclass or variant of ChartPanel — completely separate so nothing here can
// ever affect the live chart's behavior, even by accident.
public class SimulatedChartPanel : Panel
{
    private readonly Label _header;
    private WebView2 _webView = null!;
    private TaskCompletionSource? _readyTcs;

    public SimulatedChartPanel(string title)
    {
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

            _readyTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
        }
    }

    private bool _visibleDaysSet;

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
    }

    private async Task RunScriptAsync(string jsFunction, List<CandleData> candles)
    {
        if (_webView.CoreWebView2 == null) return;
        await _webView.CoreWebView2.ExecuteScriptAsync($"{jsFunction}({ToChartJson(candles)});");
    }

    // Same "ET wall-clock digits disguised as UTC" trick ChartPanel uses (see its
    // FakeUtcEpochSeconds) — Lightweight Charts renders the Unix timestamp as literal UTC digits.
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static string ToChartJson(List<CandleData> candles)
    {
        object Map(CandleData c)
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

        return System.Text.Json.JsonSerializer.Serialize(candles.Select(Map));
    }
}
