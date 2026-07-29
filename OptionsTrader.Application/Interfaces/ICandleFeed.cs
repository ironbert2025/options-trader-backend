using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.Application.Interfaces;

// Common event surface for "something that pushes live candle ticks" — implemented by
// SchwabStreamerClient (the real Schwab WebSocket) and by a local relay client used by
// secondary app instances that don't own the one Schwab streaming connection allowed per
// account. Consumers (chart panels) depend on this instead of a concrete streamer type, so they
// don't need to know which one is actually feeding them.
public interface ICandleFeed
{
    event Action<string, CandleData>? OnNewCandle;

    // Fires on every raw LEVEL_ONE_EQUITIES last-price update (much higher frequency than
    // CHART_EQUITY's one-per-minute bar) — symbol, last price, UTC trade time. Lets consumers
    // (chart panels) update the currently-forming candle's Close in near real time instead of
    // waiting for the next 1-minute bar, without changing who owns bar Open/High/Low/boundaries.
    event Action<string, decimal, DateTime>? OnLevelOneTick;

    event Action<string>? OnDisconnected;
}
