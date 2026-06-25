using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.Application.Interfaces;

public interface ISchwabMarketDataService
{
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly expiration);

    // Fetches all expirations in [fromDate, toDate] in a single request so every contract
    // shares the same underlying (spot) price snapshot.
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly fromDate, DateOnly toDate);
}
