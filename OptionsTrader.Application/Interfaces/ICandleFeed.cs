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
    event Action<string>? OnDisconnected;
}
