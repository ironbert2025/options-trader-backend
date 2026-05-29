using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Domain.Entities;

public class Trade
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OptionType OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal SpotPrice { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly TradeDate { get; set; }
    public BrokerName Broker { get; set; }

    public ICollection<Screenshot> Screenshots { get; set; } = new List<Screenshot>();
}
