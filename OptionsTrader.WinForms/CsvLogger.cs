using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.WinForms;

/// <summary>
/// Logs real-time options quotes to CSV files during a polling session.
/// Creates one file for Calls and one for Puts.
/// Appends to existing files if they already exist (same day + same expDate).
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private const string OutputFolder = @"C:\OptionsData";
    private const string Header = "Time,Hour,Minutes,Seconds,SpotPrice,StrikePrice,Bid,Ask";

    private StreamWriter? _callWriter;
    private StreamWriter? _putWriter;
    private bool _disposed;

    public void Open(string symbol, DateOnly runDate, DateOnly expDate)
    {
        Directory.CreateDirectory(OutputFolder);

        var callPath = BuildPath(symbol, "Call", runDate, expDate);
        var putPath  = BuildPath(symbol, "Put",  runDate, expDate);

        bool callExists = File.Exists(callPath);
        bool putExists  = File.Exists(putPath);

        _callWriter = new StreamWriter(callPath, append: true) { AutoFlush = true };
        _putWriter  = new StreamWriter(putPath,  append: true) { AutoFlush = true };

        // Write header only if file is new
        if (!callExists) _callWriter.WriteLine(Header);
        if (!putExists)  _putWriter.WriteLine(Header);
    }

    public void AppendRows(IEnumerable<OptionQuoteDto> quotes)
    {
        if (_callWriter == null || _putWriter == null) return;

        var now = DateTime.Now;
        var timeStr = now.ToString("HH:mm:ss");
        var hour    = now.Hour;
        var min     = now.Minute;
        var sec     = now.Second;

        foreach (var q in quotes)
        {
            // Skip rows where Bid == 0
            if (q.Bid == 0) continue;

            var line = $"{timeStr},{hour},{min},{sec},{q.SpotPrice},{q.StrikePrice},{q.Bid},{q.Ask}";

            if (q.OptionType == OptionType.Call)
                _callWriter.WriteLine(line);
            else
                _putWriter.WriteLine(line);
        }
    }

    public void Close()
    {
        _callWriter?.Flush();
        _callWriter?.Close();
        _putWriter?.Flush();
        _putWriter?.Close();
        _callWriter = null;
        _putWriter  = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
    }

    private static string BuildPath(string symbol, string type, DateOnly runDate, DateOnly expDate)
    {
        var fileName = $"{symbol}_{type}_{runDate:yyyyMMdd}_{expDate:yyyyMMdd}.csv";
        return Path.Combine(OutputFolder, fileName);
    }
}
