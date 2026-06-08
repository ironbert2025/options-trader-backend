namespace OptionsTrader.Application.DTOs.Screenshots;

public class CreateScreenshotDto
{
    public int TradeId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string S3Url { get; set; } = string.Empty;
}
