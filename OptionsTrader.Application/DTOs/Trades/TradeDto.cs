using OptionsTrader.Application.DTOs.Screenshots;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Application.DTOs.Trades;

public class TradeDto
{
    public int Id { get; set; }
    public int DailyTradeNumber { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OptionType OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal SpotPrice { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly TradeDate { get; set; }
    public DateTime EntryTime { get; set; }
    public int Contracts { get; set; }
    public int Level { get; set; }
    public decimal TargetPercent { get; set; }
    public TimeSpan? Duration { get; set; }
    public decimal? Pnl { get; set; }
    public decimal? PnlPercent { get; set; }
    public bool IsDemo { get; set; }
    public BrokerName Broker { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public IEnumerable<ScreenshotDto> Screenshots { get; set; } = [];
}
