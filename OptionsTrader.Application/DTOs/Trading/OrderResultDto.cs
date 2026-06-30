namespace OptionsTrader.Application.DTOs.Trading;

// Result of placing or querying a Schwab order.
public class OrderResultDto
{
    public long OrderId { get; set; }
    public string Status { get; set; } = string.Empty;   // e.g. WORKING, FILLED, REJECTED
    public decimal? FilledPrice { get; set; }            // average fill price once executed
    public int FilledQuantity { get; set; }
}
