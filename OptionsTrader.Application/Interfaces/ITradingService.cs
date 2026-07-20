using OptionsTrader.Application.DTOs.Trading;

namespace OptionsTrader.Application.Interfaces;

// Broker-agnostic contract for account lookup and order placement. Implemented by
// SchwabTradingService today; other brokers (IBKR, ETrade) can plug in their own
// implementation without touching callers.
public interface ITradingService
{
    // Lists the linked brokerage accounts.
    Task<IEnumerable<BrokerAccountDto>> GetAccountNumbersAsync();

    // Sends a single-leg option order to market and returns the new order id.
    // instruction is "BUY_TO_OPEN" or "SELL_TO_CLOSE".
    Task<long> PlaceOptionMarketOrderAsync(string accountId, string occSymbol, string instruction, int quantity);

    // Sends a single-leg option LIMIT order (used for the take-profit / target exit).
    Task<long> PlaceOptionLimitOrderAsync(string accountId, string occSymbol, string instruction, int quantity, decimal limitPrice);

    // Queries an order to read its status and fill price.
    Task<OrderResultDto> GetOrderAsync(string accountId, long orderId);

    // Cancels a working order (e.g. a pending Trade-Target LIMIT exit) before replacing it.
    Task CancelOrderAsync(string accountId, long orderId);
}
