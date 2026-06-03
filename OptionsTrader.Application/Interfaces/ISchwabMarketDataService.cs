using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.Application.Interfaces;

public interface ISchwabMarketDataService
{
    Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly expiration);
}
