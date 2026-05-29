namespace OptionsTrader.Domain.Entities;

public class Screenshot
{
    public int Id { get; set; }
    public int TradeId { get; set; }
    public string S3Url { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public string Symbol { get; set; } = string.Empty;

    public Trade Trade { get; set; } = null!;
}
