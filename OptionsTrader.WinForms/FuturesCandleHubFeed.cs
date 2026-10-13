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

    // Loads the REST history, once. A candle whose period already ended is completed; the one
    // containing "now" is the forming one — merged with what the ticks built there meanwhile, so
    // the load never loses a tick-observed high/low or the latest close.
    public void Seed(IEnumerable<CandleData> restCandles, DateTime nowUtc)
    {
        lock (_lock)
        {
            var previousForming = _forming;
            _completed.Clear();
            _forming = null;

            foreach (var c in restCandles.OrderBy(c => c.Time))
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

// Runs on the hub instance: loads the futures history from REST ONCE, then follows the Level 1
// futures ticks to build the candles from there on (the one forming and each one that closes).
// Every client gets the state — history + candles built so far + forming — when it connects;
// after that it only needs the relayed l1 ticks. No periodic re-sync with REST, on purpose.
internal sealed class FuturesCandleHubFeed
{
    private const int HistoryDays = 10; // 10 days = ~605 candles of 15m in one request (probed)

    private readonly SchwabStreamerClient _streamer;
    private readonly CandleHubServer _hub;
    private readonly string _symbol;
    private readonly FuturesCandleBuilder _builder;
    private int _loading;
    private bool _historyLoaded;

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
        await LoadHistoryAsync();
    }

    private void OnTick(string symbol, decimal price, DateTime utcTime)
    {
        if (symbol != _symbol) return;
        if (_builder.OnTick(utcTime, price) && !_historyLoaded) _ = LoadHistoryAsync();
    }

    // The single history load. Only if it FAILED (token, network) is it tried again, at the next
    // candle boundary — never once it succeeded.
    private async Task LoadHistoryAsync()
    {
        if (_historyLoaded || Interlocked.Exchange(ref _loading, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            var candles = await _streamer.GetCandlesAsync(_symbol, 15,
                new DateTimeOffset(now.AddDays(-HistoryDays), TimeSpan.Zero), new DateTimeOffset(now.AddMinutes(1), TimeSpan.Zero));
            _builder.Seed(candles, DateTime.UtcNow);
            _historyLoaded = candles.Count > 0;
        }
        catch
        {
            // REST failed: the ticks keep building candles meanwhile. Never reaches the hub's receive loop.
        }
        finally
        {
            Interlocked.Exchange(ref _loading, 0);
        }
    }
}
