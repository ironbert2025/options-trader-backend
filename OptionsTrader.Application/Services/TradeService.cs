using OptionsTrader.Application.DTOs.Screenshots;
using OptionsTrader.Application.DTOs.Trades;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Services;

public class TradeService(ITradeRepository trades)
{
    public async Task<IEnumerable<TradeDto>> GetAllAsync()
    {
        var result = await trades.GetAllAsync();
        return result.Select(MapToDto);
    }

    public async Task<TradeDto?> GetByIdAsync(int id)
    {
        var trade = await trades.GetByIdAsync(id);
        return trade is null ? null : MapToDto(trade);
    }

    public async Task<TradeDto> CreateAsync(CreateTradeDto dto)
    {
        if (await trades.ExistsForDateAsync(DateOnly.FromDateTime(DateTime.Today)))
            throw new InvalidOperationException("A trade already exists for today.");

        var trade = new Trade
        {
            Symbol = dto.Symbol,
            OptionType = dto.OptionType,
            StrikePrice = dto.StrikePrice,
            SpotPrice = dto.SpotPrice,
            ExpirationDate = dto.ExpirationDate,
            EntryPrice = dto.EntryPrice,
            TradeDate = DateOnly.FromDateTime(DateTime.Today),
            Broker = dto.Broker
        };

        await trades.AddAsync(trade);
        await trades.SaveChangesAsync();
        return MapToDto(trade);
    }

    private static TradeDto MapToDto(Trade trade) => new()
    {
        Id = trade.Id,
        Symbol = trade.Symbol,
        OptionType = trade.OptionType,
        StrikePrice = trade.StrikePrice,
        SpotPrice = trade.SpotPrice,
        ExpirationDate = trade.ExpirationDate,
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        TradeDate = trade.TradeDate,
        Broker = trade.Broker,
        Screenshots = trade.Screenshots.Select(s => new ScreenshotDto
        {
            Id = s.Id,
            TradeId = s.TradeId,
            S3Url = s.S3Url,
            CapturedAt = s.CapturedAt,
            Symbol = s.Symbol
        })
    };
}
