using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence.Repositories;

public class ScreenshotRepository(AppDbContext db) : IScreenshotRepository
{
    public async Task<IEnumerable<Screenshot>> GetByTradeIdAsync(int tradeId) =>
        await db.Screenshots.Where(s => s.TradeId == tradeId).ToListAsync();

    public Task<int> CountByTradeIdAsync(int tradeId) =>
        db.Screenshots.CountAsync(s => s.TradeId == tradeId);

    public async Task AddAsync(Screenshot screenshot) => await db.Screenshots.AddAsync(screenshot);

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
