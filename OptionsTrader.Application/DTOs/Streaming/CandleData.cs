namespace OptionsTrader.Application.DTOs.Streaming;

// One OHLC candle for the underlying (spot), as delivered by the Schwab CHART_EQUITY
// streaming service — one per period (Schwab's streamer sends 1-minute candles).
public class CandleData
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
