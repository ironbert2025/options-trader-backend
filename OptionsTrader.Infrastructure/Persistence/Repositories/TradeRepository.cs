using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence.Repositories;

public class TradeRepository(AppDbContext db) : ITradeRepository
{
    public Task<Trade?> GetByIdAsync(int id) =>
        db.Trades.Include(t => t.Screenshots).Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IEnumerable<Trade>> GetAllAsync(int userId) =>
        await db.Trades.Include(t => t.Screenshots).Include(t => t.User)
            .Where(t => t.UserId == userId)
            .ToListAsync();

    public async Task<IEnumerable<Trade>> GetByDateAsync(DateOnly date, int userId) =>
        await db.Trades.Include(t => t.Screenshots).Include(t => t.User)
            .Where(t => t.TradeDate == date && t.UserId == userId)
            .ToListAsync();

    public async Task<IEnumerable<Trade>> GetByMonthAsync(int year, int month, int userId) =>
        await db.Trades.Include(t => t.Screenshots).Include(t => t.User)
            .Where(t => t.TradeDate.Year == year && t.TradeDate.Month == month && t.UserId == userId)
            .ToListAsync();

    public Task<bool> ExistsForDateAsync(DateOnly date, int userId) =>
        db.Trades.AnyAsync(t => t.TradeDate == date && t.UserId == userId);

    public async Task<int> NextDailyTradeNumberAsync(DateOnly date, int userId)
    {
        var max = await db.Trades
            .Where(t => t.TradeDate == date && t.UserId == userId)
            .MaxAsync(t => (int?)t.DailyTradeNumber) ?? 0;
        return max + 1;
    }

    public async Task AddAsync(Trade trade) => await db.Trades.AddAsync(trade);

    public Task UpdateAsync(Trade trade)
    {
        db.Trades.Update(trade);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
