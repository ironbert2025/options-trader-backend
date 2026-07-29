using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.WinForms;

// Schwab allows only ONE streaming connection per account, but the user wants one app instance
// per ticker (each showing that ticker's own options chain + chart) — including instances on
// OTHER computers on the same LAN. So only ONE instance ("the hub") actually opens the Schwab
// WebSocket; it rebroadcasts every candle over a TCP socket, and the other instances ("clients")
// read from that instead of Schwab directly. No auth/handshake needed — it's newline-delimited
// JSON. Binds to IPAddress.Any (all network interfaces), not just loopback, so both same-machine
// clients (via 127.0.0.1) and clients on other LAN machines (via this machine's real IP) can
// connect to the same hub at once.
public sealed class CandleHubServer : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    // Tries to become the hub by binding the fixed port on all network interfaces. Returns false
    // if another instance ON THIS MACHINE already owns it (that instance is already the hub) —
    // the caller should fall back to CandleHubClient in that case, not treat this as an error.
    public bool TryStart(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
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
        var json = JsonSerializer.Serialize(new
        {
            type  = "candle",
            symbol,
            time  = new DateTimeOffset(DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            open  = candle.Open,
            high  = candle.High,
            low   = candle.Low,
            close = candle.Close
        });
        BroadcastLine(json);
    }

    // Relays a LEVEL_ONE_EQUITIES last-price tick to every connected client — same wire, tagged
    // with "type":"l1" so CandleHubClient can tell it apart from a candle update.
    public void BroadcastLevelOne(string symbol, decimal price, DateTime utcTime)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "l1",
            symbol,
            time = new DateTimeOffset(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            price
        });
        BroadcastLine(json);
    }

    private void BroadcastLine(string json)
    {
        List<TcpClient> snapshot;
        lock (_lock)
        {
            if (_clients.Count == 0) return;
            snapshot = _clients.ToList();
        }

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

// Connects to another instance's CandleHubServer — on this same machine (localhost) by default,
// or on another computer on the LAN (given its IP) — and re-raises each relayed candle as
// OnNewCandle. Same event shape as SchwabStreamerClient, so ChartPanel/MultiChartForm don't need
// to know whether they're fed by the real Schwab socket or a relay, local or remote.
public sealed class CandleHubClient : ICandleFeed, IAsyncDisposable
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private volatile bool _stopRequested;
    private int _port;
    private string _host = "127.0.0.1";

    public event Action<string, CandleData>? OnNewCandle;
    public event Action<string, decimal, DateTime>? OnLevelOneTick;
    public event Action<string>? OnDisconnected;

    // host: "127.0.0.1" (default) for a hub on this same machine, or the hub machine's LAN IP
    // (e.g. "192.168.1.50") to read a hub running on another computer on the network.
    public async Task ConnectAsync(int port, string host = "127.0.0.1", CancellationToken ct = default)
    {
        _port = port;
        _host = host;
        _client = new TcpClient();
        await _client.ConnectAsync(_host, port, ct);

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
                await _client.ConnectAsync(_host, _port);
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

            // "type" is absent on messages from hub instances built before this field existed —
            // treat that as "candle" too, so an old hub / new client pairing still works.
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "candle";
            var time = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("time").GetInt64()).UtcDateTime;

            if (type == "l1")
            {
                OnLevelOneTick?.Invoke(symbol, root.GetProperty("price").GetDecimal(), time);
                return;
            }

            var candle = new CandleData
            {
                Time  = time,
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
