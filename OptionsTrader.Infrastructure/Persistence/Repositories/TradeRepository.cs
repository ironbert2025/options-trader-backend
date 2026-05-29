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

    public Task<bool> ExistsForDateAsync(DateOnly date) =>
        db.Trades.AnyAsync(t => t.TradeDate == date);

    public async Task AddAsync(Trade trade) => await db.Trades.AddAsync(trade);

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
