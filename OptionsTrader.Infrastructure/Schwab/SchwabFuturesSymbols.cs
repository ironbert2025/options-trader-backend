namespace OptionsTrader.Infrastructure.Schwab;

// Futures contracts the streamer asks Schwab for (LEVELONE_FUTURES), hardcoded for now. A contract
// expires, so this has to be rolled by hand (Z = December, 26 = 2026): MES = Micro E-mini S&P 500,
// MYM = Micro E-mini Dow, MNQ = Micro E-mini Nasdaq-100, M2K = Micro E-mini Russell 2000.
// The exact key format Schwab's streamer accepts ("/MESZ26" vs "/MESZ6") is not documented — check the
// ADD response in ws_raw.log / the status line on first use.
public static class SchwabFuturesSymbols
{
    public static readonly string[] Symbols = { "/MESZ26", "/MYMZ26", "/MNQZ26", "/M2KZ26" };

    // Futures keys carry a leading slash; equities never do.
    public static bool IsFutures(string symbol) => symbol.StartsWith('/');
}
