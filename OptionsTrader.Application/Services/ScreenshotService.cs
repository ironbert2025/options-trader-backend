using OptionsTrader.Application.DTOs.Screenshots;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Services;

public class ScreenshotService(IScreenshotRepository screenshotRepo, IScreenshotStorage storage)
{
    private const int MaxScreenshotsPerTrade = 3;

    public async Task<ScreenshotDto> AddAsync(int tradeId, string symbol, Stream imageStream, string fileName)
    {
        var count = await screenshotRepo.CountByTradeIdAsync(tradeId);
        if (count >= MaxScreenshotsPerTrade)
            throw new InvalidOperationException($"A trade cannot have more than {MaxScreenshotsPerTrade} screenshots.");

        var s3Url = await storage.UploadAsync(imageStream, fileName);

        var screenshot = new Screenshot
        {
            TradeId = tradeId,
            Symbol = symbol,
            S3Url = s3Url,
            CapturedAt = DateTime.UtcNow
        };

        await screenshotRepo.AddAsync(screenshot);
        await screenshotRepo.SaveChangesAsync();

        return new ScreenshotDto
        {
            Id = screenshot.Id,
            TradeId = screenshot.TradeId,
            S3Url = screenshot.S3Url,
            CapturedAt = screenshot.CapturedAt,
            Symbol = screenshot.Symbol
        };
    }

    public async Task<IEnumerable<ScreenshotDto>> GetByTradeAsync(int tradeId)
    {
        var result = await screenshotRepo.GetByTradeIdAsync(tradeId);
        return result.Select(s => new ScreenshotDto
        {
            Id = s.Id,
            TradeId = s.TradeId,
            S3Url = s.S3Url,
            CapturedAt = s.CapturedAt,
            Symbol = s.Symbol
        });
    }
}
