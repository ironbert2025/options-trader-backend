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
            if (history.Count > 0)
                await RunScriptAsync("cargarHistorial", history);

            await _streamer.ConnectAsync();
            await _streamer.SubscribeChartEquity(_symbol);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the live chart for {_symbol}:\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private static string ToChartJson(object payload)
    {
        static object Map(CandleData c) => new
        {
            time  = new DateTimeOffset(c.Time).ToUnixTimeSeconds(),
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

    private async void ChartForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _closing = true;
        _streamer.OnNewCandle    -= Streamer_OnNewCandle;
        _streamer.OnDisconnected -= Streamer_OnDisconnected;
        await _streamer.DisposeAsync();
    }
}
