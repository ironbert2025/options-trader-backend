using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.Infrastructure.Schwab;

// Hand-rolled client for Schwab's streaming (WebSocket) API — there is no official .NET SDK for
// it. Handles: fetching streamer connection info via REST, connecting, logging in, subscribing
// to CHART_EQUITY (underlying candles), and reconnecting with backoff if the socket drops.
//
// Message shapes (LOGIN/ADD request/response, CHART_EQUITY content field numbers) have been
// confirmed against live traffic — see LogRawMessage/ws_raw.log for the raw dump this was
// validated with.
public class SchwabStreamerClient : ICandleFeed, IAsyncDisposable
{
    private const string UserPreferenceUrl = "https://api.schwabapi.com/trader/v1/userPreference";
    private const string PriceHistoryUrl   = "https://api.schwabapi.com/marketdata/v1/pricehistory";

    private readonly HttpClient _httpClient;
    private readonly SchwabAuthService _authService;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _refreshToken;
    private readonly Func<string, DateTime, Task> _onTokenRenewed;

    private string _storedAccessToken;
    private DateTime _storedExpiresAt;

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;

    private string _streamerSocketUrl = string.Empty;
    private string _schwabClientCustomerId = string.Empty;
    private string _schwabClientCorrelId = string.Empty;
    private string _schwabClientChannel = string.Empty;
    private string _schwabClientFunctionId = string.Empty;
    private int _requestId;
    private volatile bool _stopRequested;

    // Completed by HandleMessage when the LOGIN response arrives, so ConnectAsync can wait for
    // an actual server-side ack instead of just firing the LOGIN request and returning immediately
    // — sending ADD before LOGIN is acknowledged gets rejected with "STREAM CONNECTION NOT FOUND".
    private TaskCompletionSource? _loginTcs;

    // Every symbol ever passed to SubscribeChartEquity, so a reconnect can automatically
    // re-ADD all of them on the new socket — Schwab drops all subscriptions on disconnect.
    private readonly HashSet<string> _subscribedSymbols = new();

    // Fires once per candle update the streamer sends (Schwab's CHART_EQUITY service pushes a
    // new/updated 1-minute candle per subscribed symbol — one connection can carry several
    // symbols at once, so the symbol is included for the caller to route on).
    public event Action<string, CandleData>? OnNewCandle;

    // Fires when the socket disconnects unexpectedly (not via StopAsync), before a reconnect
    // attempt — useful for surfacing a status message in the UI.
    public event Action<string>? OnDisconnected;

    public SchwabStreamerClient(
        HttpClient httpClient,
        SchwabAuthService authService,
        string apiKey,
        string apiSecret,
        string refreshToken,
        string storedAccessToken,
        DateTime storedExpiresAt,
        Func<string, DateTime, Task> onTokenRenewed)
    {
        _httpClient        = httpClient;
        _authService       = authService;
        _apiKey            = apiKey;
        _apiSecret         = apiSecret;
        _refreshToken      = refreshToken;
        _storedAccessToken = storedAccessToken;
        _storedExpiresAt   = storedExpiresAt;
        _onTokenRenewed    = onTokenRenewed;
    }

    private async Task OnTokenRenewedInternal(string newAccessToken, DateTime newExpiresAt)
    {
        _storedAccessToken = newAccessToken;
        _storedExpiresAt   = newExpiresAt;
        await _onTokenRenewed(newAccessToken, newExpiresAt);
    }

    private Task<string> GetTokenAsync() =>
        _authService.GetAccessTokenAsync(
            _apiKey, _apiSecret,
            _storedAccessToken, _storedExpiresAt,
            _refreshToken, OnTokenRenewedInternal);

    // Fetches streamerSocketUrl + streamer credentials via the REST "User Preference" endpoint,
    // then opens the WebSocket and logs in. Call SubscribeChartEquity afterwards.
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // A previous socket left open (e.g. from a reconnect after a drop) still counts as "a
        // connection" on Schwab's side — logging in again before it's actually torn down gets
        // rejected with "another connection for this username and password", which then makes
        // THIS attempt fail too, in a self-inflicted loop. Abort() is used instead of a graceful
        // CloseAsync because the old socket may already be in a broken state that can't complete
        // a close handshake.
        try { _socket?.Abort(); } catch { /* best effort */ }
        try { _socket?.Dispose(); } catch { /* best effort */ }
        _socket = null;

        await FetchStreamerInfoAsync(ct);

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(_streamerSocketUrl), ct);

        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token));

        _loginTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await LoginAsync(ct);

        // Wait for the server's LOGIN response before returning — SubscribeChartEquity called
        // too early (before LOGIN is acknowledged) gets rejected with "STREAM CONNECTION NOT
        // FOUND - Please login again." even though the request was sent successfully.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        using (linkedCts.Token.Register(() => _loginTcs.TrySetCanceled()))
        {
            await _loginTcs.Task;
        }
    }

    // Seeds the chart with the last `days` TRADING days of 1-minute candles so it isn't empty
    // when first opened (the streamer only pushes candles going forward from the moment it
    // connects).
    //
    // Uses explicit startDate/endDate (epoch ms) instead of periodType=day&period=N — Schwab
    // documents that period-based day ranges compute startDate as "endDate - period, EXCLUDING
    // weekends and holidays", which silently drops Saturday/Sunday overnight-session candles no
    // matter how large `days` is. But a plain calendar-day startDate has the opposite problem:
    // requesting "3 days" from a Monday would land on Friday, giving only ~1 trading day of
    // actual session data plus 2 weekend days with no regular session. So startDate is computed
    // by walking back `days` WEEKDAYS (Mon-Fri) — e.g. "3 days" from a Monday reaches back to
    // Wednesday — while the explicit date range still naturally includes any weekend overnight
    // session that falls inside it.
    public async Task<List<CandleData>> GetHistoricalCandlesAsync(string symbol, int days, CancellationToken ct = default)
    {
        var token = await GetTokenAsync();
        var endDate   = DateTimeOffset.UtcNow;
        var startDate = ComputeStartDate(endDate, days);
        var url = $"{PriceHistoryUrl}?symbol={symbol}&periodType=day&frequencyType=minute&frequency=1" +
                  $"&startDate={startDate.ToUnixTimeMilliseconds()}&endDate={endDate.ToUnixTimeMilliseconds()}" +
                  $"&needExtendedHoursData=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonDocument.Parse(json).RootElement;

        var candles = new List<CandleData>();
        if (!root.TryGetProperty("candles", out var candlesArray)) return candles;

        foreach (var c in candlesArray.EnumerateArray())
        {
            candles.Add(new CandleData
            {
                Open  = c.GetProperty("open").GetDecimal(),
                High  = c.GetProperty("high").GetDecimal(),
                Low   = c.GetProperty("low").GetDecimal(),
                Close = c.GetProperty("close").GetDecimal(),
                Time  = DateTimeOffset.FromUnixTimeMilliseconds(c.GetProperty("datetime").GetInt64()).UtcDateTime
            });
        }
        return candles;
    }

    // Walks back `tradingDays` weekdays (Mon-Fri) from `from`, so the resulting range always
    // covers that many actual trading sessions regardless of how many weekend days it spans.
    private static DateTimeOffset ComputeStartDate(DateTimeOffset from, int tradingDays)
    {
        var date = from.Date;
        var counted = 0;
        while (counted < tradingDays)
        {
            date = date.AddDays(-1);
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                counted++;
        }
        return new DateTimeOffset(date, from.Offset);
    }

    private async Task FetchStreamerInfoAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, UserPreferenceUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonDocument.Parse(json).RootElement;

        // { "streamerInfo": [ { "streamerSocketUrl": "...", "schwabClientCustomerId": "...",
        //   "schwabClientCorrelId": "...", "schwabClientChannel": "...", "schwabClientFunctionId": "..." } ] }
        var info = root.GetProperty("streamerInfo")[0];
        _streamerSocketUrl          = info.GetProperty("streamerSocketUrl").GetString() ?? string.Empty;
        _schwabClientCustomerId     = info.GetProperty("schwabClientCustomerId").GetString() ?? string.Empty;
        _schwabClientCorrelId       = info.GetProperty("schwabClientCorrelId").GetString() ?? string.Empty;
        _schwabClientChannel        = info.GetProperty("schwabClientChannel").GetString() ?? string.Empty;
        _schwabClientFunctionId     = info.GetProperty("schwabClientFunctionId").GetString() ?? string.Empty;
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync();
        var payload = new
        {
            requests = new[]
            {
                new
                {
                    service   = "ADMIN",
                    command   = "LOGIN",
                    requestid = NextRequestId(),
                    SchwabClientCustomerId = _schwabClientCustomerId,
                    SchwabClientCorrelId   = _schwabClientCorrelId,
                    parameters = new
                    {
                        Authorization = token,
                        SchwabClientChannel    = _schwabClientChannel,
                        SchwabClientFunctionId = _schwabClientFunctionId
                    }
                }
            }
        };

        await SendAsync(payload, ct);
    }

    // Subscribes to 1-minute candles for one or more underlyings on this SAME connection —
    // Schwab's CHART_EQUITY ADD accepts a comma-separated "keys" list, so all symbols share the
    // one streaming connection Schwab allows per account. CHART_EQUITY field mapping confirmed
    // against live traffic (key/seq come back as named properties, not numbered fields):
    // 1=duplicate of seq (unused), 2=open, 3=high, 4=low, 5=close, 6=volume, 7=chartTime (epoch ms), 8=chartDay.
    public Task SubscribeChartEquity(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var symbolList = symbols.ToList();
        foreach (var s in symbolList) _subscribedSymbols.Add(s);

        var payload = new
        {
            requests = new[]
            {
                new
                {
                    service   = "CHART_EQUITY",
                    command   = "ADD",
                    requestid = NextRequestId(),
                    SchwabClientCustomerId = _schwabClientCustomerId,
                    SchwabClientCorrelId   = _schwabClientCorrelId,
                    parameters = new
                    {
                        keys   = string.Join(",", symbolList),
                        fields = "0,1,2,3,4,5,6,7,8"
                    }
                }
            }
        };
        return SendAsync(payload, ct);
    }

    public Task SubscribeChartEquity(string symbol, CancellationToken ct = default) =>
        SubscribeChartEquity(new[] { symbol }, ct);

    private int NextRequestId() => Interlocked.Increment(ref _requestId);

    private async Task SendAsync(object payload, CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Streamer socket is not connected.");

        var json  = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (_socket is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(messageStream.ToArray());
                LogRawMessage(json);
                HandleMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync/Dispose — not a real disconnect.
            return;
        }
        catch
        {
            // Any other failure means the socket dropped unexpectedly — reconnect below.
        }

        if (!_stopRequested)
            await ReconnectWithBackoffAsync();
    }

    // TEMPORARY: dumps every raw message received from the streamer to disk, one line per
    // message, so the actual Schwab wire format can be inspected and compared against the
    // documented shape this client's parsing assumes. Remove once confirmed against live traffic.
    private const string RawLogPath = @"C:\OptionsTraderPush\ws_raw.log";
    private static readonly object RawLogLock = new();

    private static void LogRawMessage(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RawLogPath)!);
            lock (RawLogLock)
            {
                File.AppendAllText(RawLogPath, $"[{DateTime.Now:O}] {json}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never let logging break the receive loop.
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var root = JsonDocument.Parse(json).RootElement;

            // Command ack payload shape (confirmed against live traffic):
            // { "response": [ { "service": "ADMIN", "command": "LOGIN", "content": { "code": 0, "msg": "..." } } ] }
            if (root.TryGetProperty("response", out var responseArray))
            {
                foreach (var entry in responseArray.EnumerateArray())
                {
                    if (!entry.TryGetProperty("service", out var svc) || svc.GetString() != "ADMIN") continue;
                    if (!entry.TryGetProperty("command", out var cmd) || cmd.GetString() != "LOGIN") continue;

                    var code = entry.TryGetProperty("content", out var c) && c.TryGetProperty("code", out var codeEl)
                        ? codeEl.GetInt32() : -1;
                    var msg = entry.TryGetProperty("content", out var c2) && c2.TryGetProperty("msg", out var msgEl)
                        ? msgEl.GetString() : null;

                    if (code == 0) _loginTcs?.TrySetResult();
                    else _loginTcs?.TrySetException(new InvalidOperationException($"Streamer LOGIN failed ({code}): {msg}"));
                }
                return;
            }

            // Data payload shape: { "data": [ { "service": "CHART_EQUITY", "content": [ {...} ] } ] }
            if (!root.TryGetProperty("data", out var dataArray)) return;

            foreach (var entry in dataArray.EnumerateArray())
            {
                if (!entry.TryGetProperty("service", out var svc) || svc.GetString() != "CHART_EQUITY")
                    continue;
                if (!entry.TryGetProperty("content", out var content)) continue;

                foreach (var item in content.EnumerateArray())
                {
                    var symbol = item.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null;
                    if (string.IsNullOrEmpty(symbol)) continue;

                    var candle = new CandleData
                    {
                        Open  = GetDecimal(item, "2"),
                        High  = GetDecimal(item, "3"),
                        Low   = GetDecimal(item, "4"),
                        Close = GetDecimal(item, "5"),
                        Time  = item.TryGetProperty("7", out var t) && t.TryGetInt64(out var epochMs)
                            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime
                            : DateTime.UtcNow
                    };
                    OnNewCandle?.Invoke(symbol, candle);
                }
            }
        }
        catch
        {
            // Malformed/unexpected message (e.g. heartbeat, notify) — ignore, not fatal.
        }
    }

    private static decimal GetDecimal(JsonElement item, string field) =>
        item.TryGetProperty(field, out var v) && v.TryGetDecimal(out var d) ? d : 0m;

    // Retries forever (never gives up — the app should keep trying for as long as it's open
    // during market hours) with a backoff that actually escalates across repeated failures.
    // Each ReceiveLoopAsync exit calls this as a fresh method invocation, so the step index is
    // kept on the instance (_reconnectAttempt) instead of a local array walked once per call —
    // otherwise every failed attempt would restart at the shortest delay forever, hammering the
    // server every ~2s instead of actually backing off.
    private static readonly int[] ReconnectDelaysMs = { 2000, 5000, 10000, 20000, 30000 };
    private int _reconnectAttempt;

    private async Task ReconnectWithBackoffAsync()
    {
        OnDisconnected?.Invoke("Streamer disconnected — reconnecting...");
        while (!_stopRequested)
        {
            var delay = ReconnectDelaysMs[Math.Min(_reconnectAttempt, ReconnectDelaysMs.Length - 1)];
            _reconnectAttempt++;
            await Task.Delay(delay);
            if (_stopRequested) return;

            try
            {
                await ConnectAsync();
                if (_subscribedSymbols.Count > 0)
                    await SubscribeChartEquity(_subscribedSymbols.ToList());
                _reconnectAttempt = 0;
                return;
            }
            catch
            {
                // Keep retrying at the (now escalated) next delay step.
            }
        }
    }

    public async Task StopAsync()
    {
        _stopRequested = true;
        _receiveLoopCts?.Cancel();

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
            catch
            {
                // Best-effort close — socket may already be in a bad state.
            }
        }

        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask; } catch { /* already handled inside the loop */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _socket?.Dispose();
        _receiveLoopCts?.Dispose();
    }
}
