using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence.Repositories;

public class TradeRepository(AppDbContext db) : ITradeRepository
{
    public Task<Trade?> GetByIdAsync(int id) =>
        db.Trades.Include(t => t.Screenshots).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IEnumerable<Trade>> GetAllAsync() =>
        await db.Trades.Include(t => t.Screenshots).ToListAsync();

    public async Task<IEnumerable<Trade>> GetByDateAsync(DateOnly date) =>
        await db.Trades.Include(t => t.Screenshots).Where(t => t.TradeDate == date).ToListAsync();

    public async Task<IEnumerable<Trade>> GetByMonthAsync(int year, int month) =>
        await db.Trades.Include(t => t.Screenshots)
            .Where(t => t.TradeDate.Year == year && t.TradeDate.Month == month)
            .ToListAsync();

    public Task<bool> ExistsForDateAsync(DateOnly date) =>
        db.Trades.AnyAsync(t => t.TradeDate == date);

    public async Task<int> NextDailyTradeNumberAsync(DateOnly date)
    {
        var max = await db.Trades
            .Where(t => t.TradeDate == date)
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
