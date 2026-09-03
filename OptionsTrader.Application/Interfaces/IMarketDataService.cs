using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.Application.Interfaces;

// Broker-agnostic contract for fetching option chain quotes. Implemented by SchwabMarketDataService
// today; other brokers (IBKR, ETrade) can plug in their own implementation without touching callers.
public interface IMarketDataService
{
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly expiration, int? strikeCount = null);

    // Fetches all expirations in [fromDate, toDate] in a single request so every contract
    // shares the same underlying (spot) price snapshot. strikeCount is PER SIDE (Schwab's own
    // chains endpoint returns this many calls AND this many puts, not split between them) —
    // null keeps the implementation's own default.
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly fromDate, DateOnly toDate, int? strikeCount = null);
}
