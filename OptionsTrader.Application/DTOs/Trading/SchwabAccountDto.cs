namespace OptionsTrader.Application.DTOs.Trading;

// A Schwab brokerage account. HashValue is the encrypted account id used for all
// trading calls; AccountNumber is the plain number, shown masked in the UI.
public class SchwabAccountDto
{
    public string AccountNumber { get; set; } = string.Empty;
    public string HashValue { get; set; } = string.Empty;
}
