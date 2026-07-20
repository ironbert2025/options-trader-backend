namespace OptionsTrader.Application.DTOs.Trading;

// A linked brokerage account. AccountId is the broker-specific identifier used for all
// trading calls (Schwab's encrypted hash value, or the equivalent for another broker);
// AccountNumber is the plain number, shown masked in the UI.
public class BrokerAccountDto
{
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
}
