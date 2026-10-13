using System.Text.Json;
using OptionsTrader.Application.DTOs.Streaming;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

// 15-minute candles of one futures contract, kept by the hub: the completed ones come from Schwab's
// pricehistory (REST — the websocket can't backfill), the one still forming is built tick by tick from
// the LEVELONE_FUTURES last price. Pure logic, no I/O, so it can be checked on its own.
internal sealed class FuturesCandleBuilder
{
    private const int MaxCompleted = 1500; // 10 days of 15m candles is ~610
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly object _lock = new();
    private readonly List<CandleData> _completed = new();
    private CandleData? _forming;

    public string Symbol { get; }
    public FuturesCandleBuilder(string symbol) => Symbol = symbol;

    // 15m buckets start at :00/:15/:30/:45 in every time zone (offsets are whole hours), so UTC works.
    public static DateTime BucketStart(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % Interval.Ticks, DateTimeKind.Utc);

    // Loads a REST window. merge=false replaces the whole history (startup); merge=true is the light
    // refresh: only candles from the window's first one onward are replaced, everything older that
    // is already held stays. A candle whose period already ended is completed; the one containing
    // "now" is the forming one — merged with what the ticks built there since, so a refresh never
    // loses a tick-observed high/low or the latest close.
    public void Seed(IEnumerable<CandleData> restCandles, DateTime nowUtc, bool merge = false)
    {
        lock (_lock)
        {
            var previousForming = _forming;
            var rest = restCandles.OrderBy(c => c.Time).ToList();
            var older = merge && rest.Count > 0
                ? _completed.Where(c => c.Time < rest[0].Time).ToList()
                : new List<CandleData>();
            _completed.Clear();
            _completed.AddRange(older);
            _forming = null;

            foreach (var c in rest)
            {
                if (c.Time + Interval <= nowUtc) _completed.Add(c);
                else if (c.Time <= nowUtc) _forming = c;
            }

            if (previousForming != null)
            {
                if (_forming != null && _forming.Time == previousForming.Time)
                {
                    _forming.High = Math.Max(_forming.High, previousForming.High);
                    _forming.Low = Math.Min(_forming.Low, previousForming.Low);
                    _forming.Close = previousForming.Close;
                }
                else if (_forming == null && previousForming.Time + Interval > nowUtc)
                {
                    _forming = previousForming; // REST doesn't have the current candle yet
                }
            }
            Trim();
        }
    }

    // Returns true when this tick started a NEW candle (the previous one just closed).
    public bool OnTick(DateTime utc, decimal price)
    {
        lock (_lock)
        {
            var bucket = BucketStart(utc);
            if (_forming == null)
            {
                _forming = NewCandle(bucket, price);
                return false;
            }
            if (bucket < _forming.Time) return false; // late/out-of-order tick
            if (bucket == _forming.Time)
            {
                _forming.High = Math.Max(_forming.High, price);
                _forming.Low = Math.Min(_forming.Low, price);
                _forming.Close = price;
                return false;
            }
            _completed.Add(_forming);
            _forming = NewCandle(bucket, price);
            Trim();
            return true;
        }
    }

    private static CandleData NewCandle(DateTime bucket, decimal price) =>
        new() { Time = bucket, Open = price, High = price, Low = price, Close = price };

    private void Trim()
    {
        if (_completed.Count > MaxCompleted) _completed.RemoveRange(0, _completed.Count - MaxCompleted);
    }

    // {"type":"candles15","symbol":"/MESZ26","interval":15,"candles":[...completed...],"forming":{...}|null}
    // time = start of the candle, ms since epoch UTC. volume is 0 on a candle built from ticks only.
    public string BuildSnapshotJson()
    {
        lock (_lock)
        {
            static object Shape(CandleData c) => new
            {
                time = new DateTimeOffset(DateTime.SpecifyKind(c.Time, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                open = c.Open, high = c.High, low = c.Low, close = c.Close, volume = c.Volume
            };
            return JsonSerializer.Serialize(new
            {
                type = "candles15",
                symbol = Symbol,
                interval = 15,
                candles = _completed.Select(Shape).ToList(),
                forming = _forming == null ? null : Shape(_forming)
            });
        }
    }
}

// Runs on the hub instance: seeds the futures candles from REST, follows the Level 1 futures ticks to
// draw the forming candle, re-syncs with REST each time a candle closes (the official OHLC + volume
// replace the tick-built ones), and hands the whole state to every client when it connects.
internal sealed class FuturesCandleHubFeed
{
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly TimeSpan RefreshDelayAfterClose = TimeSpan.FromSeconds(5);

    private readonly SchwabStreamerClient _streamer;
    private readonly CandleHubServer _hub;
    private readonly string _symbol;
    private readonly FuturesCandleBuilder _builder;
    private int _refreshing;

    public FuturesCandleHubFeed(SchwabStreamerClient streamer, CandleHubServer hub, string symbol)
    {
        _streamer = streamer;
        _hub = hub;
        _symbol = symbol;
        _builder = new FuturesCandleBuilder(symbol);
    }

    public async Task StartAsync()
    {
        _hub.SetSnapshotProvider(() => _builder.BuildSnapshotJson());
        _streamer.OnLevelOneTick += OnTick;
        await RefreshAsync(broadcast: false);
    }

    private void OnTick(string symbol, decimal price, DateTime utcTime)
    {
        if (symbol != _symbol) return;
        if (_builder.OnTick(utcTime, price)) _ = RefreshAfterCloseAsync();
    }

    private async Task RefreshAfterCloseAsync()
    {
        try { await Task.Delay(RefreshDelayAfterClose); } catch { return; }
        await RefreshAsync(broadcast: true);
    }

    // Startup loads this many days back in one request (10 days = ~605 candles, probed); every later
    // refresh only re-requests the last RefreshDays and merges them over what is already held.
    private const int HistoryDays = 10;
    private const int RefreshDays = 2;
    private bool _hasHistory; // true once a full-window load succeeded

    private async Task RefreshAsync(bool broadcast)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            var light = _hasHistory;
            var from = now.AddDays(-(light ? RefreshDays : HistoryDays));
            var candles = await _streamer.GetCandlesAsync(_symbol, 15,
                new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(now.AddMinutes(1), TimeSpan.Zero));
            _builder.Seed(candles, DateTime.UtcNow, merge: light);
            if (candles.Count > 0) _hasHistory = true;
            if (broadcast) _hub.BroadcastRawLine(_builder.BuildSnapshotJson());
        }
        catch
        {
            // REST failed (token, network, closed market): the ticks keep building the forming candle
            // and the next candle close tries again. Never let this reach the hub's receive loop.
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }
}
