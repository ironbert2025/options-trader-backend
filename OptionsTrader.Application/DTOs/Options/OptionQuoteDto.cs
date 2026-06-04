using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Application.DTOs.Options;

public class OptionQuoteDto
{
    public string Symbol { get; set; } = string.Empty;
    public OptionType OptionType { get; set; }
    public decimal SpotPrice { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public bool InTheMoney { get; set; }
}
