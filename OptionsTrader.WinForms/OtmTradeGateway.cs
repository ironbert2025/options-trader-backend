using OptionsTrader.Application.DTOs.Options;

namespace OptionsTrader.WinForms;

// One OTM (out-of-the-money) option strike, with its already-computed rank ("Level" — 1 = nearest
// to the money) among all OTM strikes on that side, matching the same ranking shown in the Quotes
// tab grid's "Level" column.
public sealed record OtmOption(OptionQuoteDto Quote, string Level);

// Bridges the OTM Call/Put buttons overlaid on the 15m RTH+Overnight chart to Form1's existing
// options-quote polling and real-order execution — implemented by Form1, consumed by ChartPanel
// (only the Fifteen_Full panel actually wires it up).
public interface IOtmTradeGateway
{
    // Fired after every options-chain poll tick with the ticker's symbol and its current closest-
    // to-money OTM calls/puts (the same set shown in the Quotes tab grid, reusing that same
    // polling data — no separate streaming/REST call). ChartPanel filters by symbol before
    // reacting, since one shared streamer/gateway instance can be feeding multiple ticker windows.
    event Action<string, IReadOnlyList<OtmOption>, IReadOnlyList<OtmOption>>? OtmOptionsUpdated;

    // Places a REAL market BUY_TO_OPEN order for the given strike — same execution path as
    // clicking a row in the Quotes tab grid (position-size-based quantity, confirmation dialog if
    // enabled, trade recorded in the Trades grid). Always a plain entry (no auto-exit target),
    // matching the single-click behavior requested for the chart buttons.
    Task ExecuteOtmMarketOrderAsync(string rowType, decimal strike, string level, decimal bid, decimal ask);
}
