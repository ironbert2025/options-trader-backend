namespace OptionsTrader.WinForms;

public static class OccOptionSymbol
{
    // Builds the Schwab/OCC option symbol:
    //   6-char root (left-justified, space-padded) + YYMMDD + C/P + strike*1000 (8 digits).
    // Example: Build("AAPL", 2024-06-21, "CALL", 190m) => "AAPL  240621C00190000"
    //          Build("SPY",  2026-06-26, "PUT",  292.5m) => "SPY   260626P00292500"
    public static string Build(string underlying, DateOnly expiration, string optionType, decimal strike)
    {
        var root      = underlying.Trim().ToUpperInvariant().PadRight(6);
        var exp       = expiration.ToString("yyMMdd");
        var cp        = optionType.Equals("PUT", StringComparison.OrdinalIgnoreCase) ? "P" : "C";
        var strikeStr = ((long)Math.Round(strike * 1000m)).ToString("D8");
        return $"{root}{exp}{cp}{strikeStr}";
    }
}
