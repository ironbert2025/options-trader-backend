namespace OptionsTrader.Infrastructure.Schwab;

// Futures contracts the streamer asks Schwab for (LEVELONE_FUTURES), hardcoded for now. A contract
// expires, so this has to be rolled by hand: MESZ26 = Micro E-mini S&P 500, December 2026 (Z = Dec).
// The exact key format Schwab's streamer accepts ("/MESZ26" vs "/MESZ6") is not documented — check the
// ADD response in ws_raw.log / the status line on first use.
public static class SchwabFuturesSymbols
{
    public static readonly string[] Symbols = { "/MESZ26" };

    // Futures keys carry a leading slash; equities never do.
    public static bool IsFutures(string symbol) => symbol.StartsWith('/');
}
