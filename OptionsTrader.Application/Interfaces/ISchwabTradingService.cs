using OptionsTrader.Application.DTOs.Trading;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Application.Interfaces;

public interface ISchwabTradingService
{
    // Lists the linked brokerage accounts with their encrypted hash values.
    Task<IEnumerable<SchwabAccountDto>> GetAccountNumbersAsync();

    // Sends a single-leg option order to market and returns the new order id.
    // instruction is "BUY_TO_OPEN" or "SELL_TO_CLOSE".
    Task<long> PlaceOptionMarketOrderAsync(string accountHash, string occSymbol, string instruction, int quantity);

    // Sends a single-leg option LIMIT order (used for the take-profit / target exit).
    Task<long> PlaceOptionLimitOrderAsync(string accountHash, string occSymbol, string instruction, int quantity, decimal limitPrice);

    // Queries an order to read its status and fill price.
    Task<OrderResultDto> GetOrderAsync(string accountHash, long orderId);
}
