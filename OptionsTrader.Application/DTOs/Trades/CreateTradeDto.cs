using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Application.DTOs.Trades;

public class CreateTradeDto
{
    public string Symbol { get; set; } = string.Empty;
    public OptionType OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal SpotPrice { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public int Contracts { get; set; }
    public int Level { get; set; }
    public decimal TargetPercent { get; set; }
    public bool IsDemo { get; set; }
    public BrokerName Broker { get; set; }
}
