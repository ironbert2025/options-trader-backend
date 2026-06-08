namespace OptionsTrader.Application.DTOs.Trades;

public class CloseTradeDto
{
    public decimal ExitPrice { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public TimeSpan Duration { get; set; }
}
