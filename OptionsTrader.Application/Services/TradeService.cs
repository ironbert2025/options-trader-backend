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

    public async Task<IEnumerable<TradeDto>> GetByDateAsync(DateOnly date)
    {
        var result = await trades.GetByDateAsync(date);
        return result.Select(MapToDto);
    }

    public async Task<TradeDto?> GetByIdAsync(int id)
    {
        var trade = await trades.GetByIdAsync(id);
        return trade is null ? null : MapToDto(trade);
    }

    public async Task<TradeDto> CreateAsync(CreateTradeDto dto)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dailyNumber = await trades.NextDailyTradeNumberAsync(today);

        var trade = new Trade
        {
            DailyTradeNumber = dailyNumber,
            Symbol           = dto.Symbol,
            OptionType       = dto.OptionType,
            StrikePrice      = dto.StrikePrice,
            SpotPrice        = dto.SpotPrice,
            ExpirationDate   = dto.ExpirationDate,
            EntryPrice       = dto.EntryPrice,
            EntryTime        = dto.EntryTime,
            Contracts        = dto.Contracts,
            Level            = dto.Level,
            TargetPercent    = dto.TargetPercent,
            IsDemo           = dto.IsDemo,
            TradeDate        = today,
            Broker           = dto.Broker
        };

        await trades.AddAsync(trade);
        await trades.SaveChangesAsync();
        return MapToDto(trade);
    }

    public async Task<TradeDto> CloseAsync(int id, CloseTradeDto dto)
    {
        var trade = await trades.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Trade {id} not found.");

        trade.ExitPrice   = dto.ExitPrice;
        trade.Pnl         = dto.PnL;
        trade.PnlPercent  = dto.PnLPercent;
        trade.Duration    = dto.Duration;

        await trades.UpdateAsync(trade);
        await trades.SaveChangesAsync();
        return MapToDto(trade);
    }

    private static TradeDto MapToDto(Trade trade) => new()
    {
        Id               = trade.Id,
        DailyTradeNumber = trade.DailyTradeNumber,
        Symbol           = trade.Symbol,
        OptionType       = trade.OptionType,
        StrikePrice      = trade.StrikePrice,
        SpotPrice        = trade.SpotPrice,
        ExpirationDate   = trade.ExpirationDate,
        EntryPrice       = trade.EntryPrice,
        ExitPrice        = trade.ExitPrice,
        TradeDate        = trade.TradeDate,
        EntryTime        = trade.EntryTime,
        Contracts        = trade.Contracts,
        Level            = trade.Level,
        TargetPercent    = trade.TargetPercent,
        Duration         = trade.Duration,
        Pnl              = trade.Pnl,
        PnlPercent       = trade.PnlPercent,
        IsDemo           = trade.IsDemo,
        Broker           = trade.Broker,
        Screenshots      = trade.Screenshots.Select(s => new ScreenshotDto
        {
            Id         = s.Id,
            TradeId    = s.TradeId,
            S3Url      = s.S3Url,
            CapturedAt = s.CapturedAt,
            Symbol     = s.Symbol
        })
    };
}
