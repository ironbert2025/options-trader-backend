using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
    private readonly List<ClientConn> _clients = new();
    private readonly object _lock = new();

    // Each client has its OWN bounded queue drained by its own writer task, so BroadcastLine (called
    // from the thread that reads the Schwab socket) only enqueues and returns — it can never be held
    // up by a slow or stuck client (a blocking Write there stalled the Schwab read, the heartbeats
    // and every other client at once). A client whose queue fills, or whose single write makes no
    // progress for WriteTimeout, is disconnected; it reconnects on its own and gets a fresh snapshot.
    private const int MaxQueuedMessages = 5000;
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    private sealed class ClientConn
    {
        public required TcpClient Client { get; init; }
        public Channel<byte[]> Queue { get; } = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MaxQueuedMessages) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }
    private CancellationTokenSource? _cts;

    // One line per provider sent to EVERY client the moment it connects, before it joins the broadcast
    // list — currently one futures candles snapshot per symbol (see FuturesCandleHubFeed).
    // Keyed by symbol, so a feed re-created after the 4pm reconnect replaces its own provider.
    private readonly Dictionary<string, Func<string?>> _snapshotProviders = new();
    public void SetSnapshotProvider(string key, Func<string?> provider)
    {
        lock (_lock) { _snapshotProviders[key] = provider; }
    }

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
        _ = HeartbeatLoopAsync(_cts.Token);
        return true;
    }

    // Sent independent of whether any candles are actually flowing (overnight, weekends, or just
    // a quiet moment intraday) — gives clients something to see even when there's nothing to
    // broadcast, so CandleHubClient's own watchdog can tell "hub alive but quiet" apart from
    // "hub/relay actually dead" instead of only having candle/l1 traffic to judge by.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return; }

            BroadcastLine(JsonSerializer.Serialize(new { type = "heartbeat" }));
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var conn = new ClientConn { Client = client };
                try
                {
                    List<Func<string?>> providers;
                    lock (_lock) { providers = _snapshotProviders.Values.ToList(); }
                    foreach (var provider in providers)
                    {
                        var snapshot = provider();
                        if (string.IsNullOrEmpty(snapshot)) continue;
                        conn.Queue.Writer.TryWrite(Encoding.UTF8.GetBytes(snapshot + "\n"));
                    }
                }
                catch
                {
                    try { client.Dispose(); } catch { /* best effort */ }
                    continue;
                }
                lock (_lock) { _clients.Add(conn); }
                _ = WriteLoopAsync(conn, ct);
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

    // Relays a WS connect/disconnect/reconnect line (from the hub's own real Schwab connection)
    // to every connected client, so their Live Charts windows show the same connection history
    // as the hub's — tagged "type":"wsevent".
    public void BroadcastWsEvent(string text)
    {
        var json = JsonSerializer.Serialize(new { type = "wsevent", text });
        BroadcastLine(json);
    }

    private void BroadcastLine(string json)
    {
        List<ClientConn> snapshot;
        lock (_lock)
        {
            if (_clients.Count == 0) return;
            snapshot = _clients.ToList();
        }

        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        foreach (var conn in snapshot)
        {
            // Never blocks: a full queue means this client is not draining — drop it.
            if (!conn.Queue.Writer.TryWrite(bytes)) DropClient(conn);
        }
    }

    // One writer per client, so lines stay in order. Any failure or a stuck write ends the client.
    private async Task WriteLoopAsync(ClientConn conn, CancellationToken ct)
    {
        try
        {
            var stream = conn.Client.GetStream();
            await foreach (var bytes in conn.Queue.Reader.ReadAllAsync(ct))
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var write = stream.WriteAsync(bytes, ct).AsTask();
                var delay = Task.Delay(WriteTimeout, timeoutCts.Token);
                var finished = await Task.WhenAny(write, delay);
                timeoutCts.Cancel();
                if (finished != write) throw new TimeoutException("client not draining");
                await write;
            }
        }
        catch
        {
            // Disconnected, timed out or cancelled — fall through to DropClient.
        }
        finally
        {
            DropClient(conn);
        }
    }

    private void DropClient(ClientConn conn)
    {
        lock (_lock) { _clients.Remove(conn); }
        conn.Queue.Writer.TryComplete();
        try { conn.Client.Dispose(); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* best effort */ }
        lock (_lock)
        {
            foreach (var c in _clients) { try { c.Client.Dispose(); } catch { /* best effort */ } }
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

    // Watchdog: a plain TcpClient has no keep-alive by default, so a half-dead connection (the
    // hub process stalled, or the loopback/LAN path silently broke) never makes ReadLineAsync
    // throw or return null on its own — it just blocks forever. The hub now sends a heartbeat
    // line every 20s (CandleHubServer.HeartbeatLoopAsync) even when no candles are flowing, so
    // any healthy connection should never go this long without SOME line arriving.
    private DateTime _lastLineReceivedUtc = DateTime.UtcNow;
    private CancellationTokenSource? _watchdogCts;
    private bool _watchdogStarted;
    private static readonly TimeSpan WatchdogCheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WatchdogStaleThreshold = TimeSpan.FromSeconds(45);

    public event Action<string, CandleData>? OnNewCandle;
    public event Action<string, decimal, DateTime>? OnLevelOneTick;
    public event Action<string>? OnDisconnected;
    public event Action<string>? OnWsStatusEvent;

    // host: "127.0.0.1" (default) for a hub on this same machine, or the hub machine's LAN IP
    // (e.g. "192.168.1.50") to read a hub running on another computer on the network.
    public async Task ConnectAsync(int port, string host = "127.0.0.1", CancellationToken ct = default)
    {
        _port = port;
        _host = host;
        _client = new TcpClient();
        await _client.ConnectAsync(_host, port, ct);

        _lastLineReceivedUtc = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        // Started once for the lifetime of this client, not per-connection — it just watches
        // _lastLineReceivedUtc and forces a reconnect (via _client.Dispose()) if it goes stale,
        // regardless of which underlying TcpClient instance is currently live.
        if (!_watchdogStarted)
        {
            _watchdogStarted = true;
            _watchdogCts = new CancellationTokenSource();
            _ = Task.Run(() => WatchdogLoopAsync(_watchdogCts.Token));
        }
    }

    // Forces the same reconnect path a real disconnect already takes (ReceiveLoopAsync's catch
    // falls through to ReconnectLoopAsync) — Dispose() makes the blocked ReadLineAsync throw.
    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_stopRequested)
        {
            try { await Task.Delay(WatchdogCheckInterval, ct); }
            catch (OperationCanceledException) { return; }

            if (_stopRequested) return;
            if (DateTime.UtcNow - _lastLineReceivedUtc <= WatchdogStaleThreshold) continue;

            OnWsStatusEvent?.Invoke("Local candle hub watchdog: no data received in too long — forcing reconnect...");
            // Same reasoning as SchwabStreamerClient's watchdog — reset immediately so this loop
            // doesn't keep re-triggering every 15s while ReconnectLoopAsync's own 5s delay is
            // already in flight.
            _lastLineReceivedUtc = DateTime.UtcNow;
            try { _client?.Dispose(); } catch { /* best effort — ReceiveLoopAsync's catch handles the rest */ }
        }
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
                _lastLineReceivedUtc = DateTime.UtcNow;
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
                _lastLineReceivedUtc = DateTime.UtcNow;
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

            // "type" is absent on messages from hub instances built before this field existed —
            // treat that as "candle" too, so an old hub / new client pairing still works.
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "candle";

            // heartbeat carries nothing but proof the hub/relay is still alive — _lastLineReceivedUtc
            // was already updated by ReceiveLoopAsync before calling here, so there's nothing else
            // to do with it.
            if (type == "heartbeat") return;

            // Futures candles snapshot — meant for external readers of the hub; nothing in the app consumes it.
            if (type == "candles15") return;

            // wsevent carries no symbol/time — a connection status line, not a price update.
            if (type == "wsevent")
            {
                OnWsStatusEvent?.Invoke(root.GetProperty("text").GetString() ?? string.Empty);
                return;
            }

            var symbol = root.GetProperty("symbol").GetString();
            if (string.IsNullOrEmpty(symbol)) return;

            var time = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("time").GetInt64()).UtcDateTime;

            if (type == "l1")
            {
                RaiseOnLevelOneTick(symbol, root.GetProperty("price").GetDecimal(), time);
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
            RaiseOnNewCandle(symbol, candle);
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
        _watchdogCts?.Cancel();
        try { _client?.Close(); } catch { /* best effort */ }

        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { /* already handled inside the loop */ }
        }

        _client?.Dispose();
        _cts?.Dispose();
    }

    // Same reasoning as SchwabStreamerClient's identical helpers — OnNewCandle/OnLevelOneTick are
    // multicast events with one subscriber per ChartPanel (Hourly15/Fifteen_RTH/Fifteen_Full). A
    // plain "?.Invoke(...)" calls every subscriber as ONE chain: if any throws, every subscriber
    // registered after it silently never runs again for the rest of the session, and the outer
    // try/catch in HandleLine swallows the exception with zero trace. Invoking each individually
    // stops one bad handler from starving its siblings, and logs the real exception if it happens.
    private void RaiseOnNewCandle(string symbol, CandleData candle)
    {
        var handlers = OnNewCandle;
        if (handlers == null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, CandleData>>())
        {
            try { handler(symbol, candle); }
            catch (Exception ex) { LogHandlerException("OnNewCandle", symbol, ex); }
        }
    }

    private void RaiseOnLevelOneTick(string symbol, decimal price, DateTime tradeTime)
    {
        var handlers = OnLevelOneTick;
        if (handlers == null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, decimal, DateTime>>())
        {
            try { handler(symbol, price, tradeTime); }
            catch (Exception ex) { LogHandlerException("OnLevelOneTick", symbol, ex); }
        }
    }

    private const string HandlerExceptionLogPath = @"C:\OptionsData\EventLog\handler_exceptions.log";
    private static readonly object HandlerExceptionLogLock = new();

    private static void LogHandlerException(string eventName, string symbol, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HandlerExceptionLogPath)!);
            lock (HandlerExceptionLogLock)
            {
                File.AppendAllText(HandlerExceptionLogPath,
                    $"[{DateTime.Now:O}] {eventName} symbol={symbol}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
            }
        }
        catch { /* best-effort diagnostic logging — never let this affect the tick handler */ }
    }
}
