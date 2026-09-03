using System.Globalization;

namespace OptionsTrader.WinForms;

// Append-only log of demo trades placed inside SimulatorForm — completely separate from
// OpenTradesStore/the real Trades API. Never read back to restore state (the simulator's Trades
// grid lives only in memory for the current session, reset every time a new day is loaded) — this
// is purely a local record for reviewing past practice sessions afterward.
internal static class SimTradesStore
{
    private const string OutputFolder = @"C:\OptionsData\Simulator\Trades";
    private const string Header = "Symbol,SimDate,OptionType,StrikePrice,Contracts,EntryStepTime,EntryPrice,ExitStepTime,ExitPrice,PnL,PnLPercent";

    public static void Append(string symbol, DateOnly simDate, string optionType, decimal strikePrice, int contracts,
        DateTime entryStepTime, decimal entryPrice, DateTime exitStepTime, decimal exitPrice, decimal pnl, decimal pnlPercent)
    {
        try
        {
            var folder = Path.Combine(OutputFolder, symbol);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{symbol}_{simDate:yyyyMMdd}.csv");

            var isNew = !File.Exists(path);
            using var writer = new StreamWriter(path, append: true);
            if (isNew) writer.WriteLine(Header);
            writer.WriteLine(string.Join(',',
                symbol, simDate.ToString("yyyy-MM-dd"), optionType,
                strikePrice.ToString(CultureInfo.InvariantCulture),
                contracts,
                entryStepTime.ToString("HH:mm:ss"), entryPrice.ToString("F2", CultureInfo.InvariantCulture),
                exitStepTime.ToString("HH:mm:ss"), exitPrice.ToString("F2", CultureInfo.InvariantCulture),
                pnl.ToString("F2", CultureInfo.InvariantCulture), pnlPercent.ToString("F1", CultureInfo.InvariantCulture)));
        }
        catch
        {
            // Best-effort logging — never let this break the simulator.
        }
    }
}
