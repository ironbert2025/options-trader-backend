using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.WinForms;

// Schwab allows only ONE streaming connection per account, but the user wants one app instance
// per ticker (each showing that ticker's own options chain + chart). So only ONE instance ("the
// hub") actually opens the Schwab WebSocket; it rebroadcasts every candle over a local
// loopback-only TCP socket, and the other instances ("clients") read from that instead of Schwab
// directly. No cross-instance auth/handshake needed — it's newline-delimited JSON on localhost.
public sealed class CandleHubServer : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    // Tries to become the hub by binding the fixed local port. Returns false if another instance
    // already owns it (that instance is already the hub) — the caller should fall back to
    // CandleHubClient in that case, not treat this as an error.
    public bool TryStart(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }
        catch (SocketException)
        {
            _listener = null;
            return false;
        }

        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
        return true;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                lock (_lock) { _clients.Add(client); }
            }
        }
        catch
        {
            // Listener stopped (Dispose) or cancelled — not an error.
        }
    }

    public void Broadcast(string symbol, CandleData candle)
    {
        List<TcpClient> snapshot;
        lock (_lock)
        {
            if (_clients.Count == 0) return;
            snapshot = _clients.ToList();
        }

        var json = JsonSerializer.Serialize(new
        {
            symbol,
            time  = new DateTimeOffset(DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            open  = candle.Open,
            high  = candle.High,
            low   = candle.Low,
            close = candle.Close
        });
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        foreach (var client in snapshot)
        {
            try
            {
                client.GetStream().Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // Client disconnected — drop it.
                lock (_lock) { _clients.Remove(client); }
                try { client.Dispose(); } catch { /* best effort */ }
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* best effort */ }
        lock (_lock)
        {
            foreach (var c in _clients) { try { c.Dispose(); } catch { /* best effort */ } }
            _clients.Clear();
        }
        _cts?.Dispose();
    }
}

// Connects to another instance's CandleHubServer on localhost and re-raises each relayed candle
// as OnNewCandle — same event shape as SchwabStreamerClient, so ChartPanel/MultiChartForm don't
// need to know whether they're fed by the real Schwab socket or a local relay.
public sealed class CandleHubClient : ICandleFeed, IAsyncDisposable
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private volatile bool _stopRequested;
    private int _port;

    public event Action<string, CandleData>? OnNewCandle;
    public event Action<string>? OnDisconnected;

    public async Task ConnectAsync(int port, CancellationToken ct = default)
    {
        _port = port;
        _client = new TcpClient();
        await _client.ConnectAsync(IPAddress.Loopback, port, ct);

        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(_client!.GetStream(), Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // hub instance closed
                HandleLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            return; // expected on DisposeAsync
        }
        catch
        {
            // Connection dropped — fall through to reconnect below.
        }

        if (!_stopRequested)
        {
            OnDisconnected?.Invoke("Local candle hub disconnected — reconnecting...");
            await ReconnectLoopAsync();
        }
    }

    // No cross-instance failover (by design — only the original hub instance re-establishes
    // Schwab) — this just keeps retrying to reach that SAME hub in case it comes back (e.g. its
    // window was minimized/froze briefly), on a fixed 5s interval forever.
    private async Task ReconnectLoopAsync()
    {
        while (!_stopRequested)
        {
            await Task.Delay(5000);
            if (_stopRequested) return;

            try
            {
                _client?.Dispose();
                _client = new TcpClient();
                await _client.ConnectAsync(IPAddress.Loopback, _port);
                _cts = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return;
            }
            catch
            {
                // Keep retrying.
            }
        }
    }

    private void HandleLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var symbol = root.GetProperty("symbol").GetString();
            if (string.IsNullOrEmpty(symbol)) return;

            var candle = new CandleData
            {
                Time  = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("time").GetInt64()).UtcDateTime,
                Open  = root.GetProperty("open").GetDecimal(),
                High  = root.GetProperty("high").GetDecimal(),
                Low   = root.GetProperty("low").GetDecimal(),
                Close = root.GetProperty("close").GetDecimal()
            };
            OnNewCandle?.Invoke(symbol, candle);
        }
        catch
        {
            // Malformed line — ignore, not fatal.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopRequested = true;
        _cts?.Cancel();
        try { _client?.Close(); } catch { /* best effort */ }

        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { /* already handled inside the loop */ }
        }

        _client?.Dispose();
        _cts?.Dispose();
    }
}
