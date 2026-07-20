using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.Application.Interfaces;

// Broker-agnostic contract for fetching option chain quotes. Implemented by SchwabMarketDataService
// today; other brokers (IBKR, ETrade) can plug in their own implementation without touching callers.
public interface IMarketDataService
{
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly expiration);

    // Fetches all expirations in [fromDate, toDate] in a single request so every contract
    // shares the same underlying (spot) price snapshot.
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly fromDate, DateOnly toDate);
}
