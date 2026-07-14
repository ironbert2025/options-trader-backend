using OptionsTrader.Application.DTOs.Screenshots;
using OptionsTrader.Application.DTOs.Trades;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Services;

public class TradeService(ITradeRepository trades)
{
    public async Task<IEnumerable<TradeDto>> GetAllAsync(int userId)
    {
        var result = await trades.GetAllAsync(userId);
        return result.Select(MapToDto);
    }

    public async Task<IEnumerable<TradeDto>> GetByMonthAsync(int year, int month, int userId)
    {
        var result = await trades.GetByMonthAsync(year, month, userId);
        return result.Select(MapToDto);
    }

    public async Task<IEnumerable<TradeDto>> GetByDateAsync(DateOnly date, int userId)
    {
        var result = await trades.GetByDateAsync(date, userId);
        return result.Select(MapToDto);
    }

    // Returns null both when the trade doesn't exist and when it belongs to another user,
    // so callers can't distinguish "not found" from "not yours" (avoids leaking existence).
    public async Task<TradeDto?> GetByIdAsync(int id, int userId)
    {
        var trade = await trades.GetByIdAsync(id);
        return trade is null || trade.UserId != userId ? null : MapToDto(trade);
    }

    public async Task<TradeDto> CreateAsync(CreateTradeDto dto, int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dailyNumber = await trades.NextDailyTradeNumberAsync(today, userId);

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
            Broker           = dto.Broker,
            UserId           = userId
        };

        await trades.AddAsync(trade);
        await trades.SaveChangesAsync();

        // Re-fetch so the User navigation (needed for Username in the DTO) is populated —
        // the in-memory `trade` only has the UserId scalar set, not the loaded entity.
        var saved = await trades.GetByIdAsync(trade.Id) ?? trade;
        return MapToDto(saved);
    }

    public async Task<TradeDto> CloseAsync(int id, CloseTradeDto dto, int userId)
    {
        var trade = await trades.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Trade {id} not found.");

        if (trade.UserId != userId)
            throw new KeyNotFoundException($"Trade {id} not found.");

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
        UserId           = trade.UserId,
        Username         = trade.User?.Username ?? string.Empty,
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
