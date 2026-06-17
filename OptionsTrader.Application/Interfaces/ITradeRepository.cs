using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface ITradeRepository
{
    Task<Trade?> GetByIdAsync(int id);
    Task<IEnumerable<Trade>> GetAllAsync();
    Task<IEnumerable<Trade>> GetByDateAsync(DateOnly date);
    Task<bool> ExistsForDateAsync(DateOnly date);
    Task<int> NextDailyTradeNumberAsync(DateOnly date);
    Task AddAsync(Trade trade);
    Task UpdateAsync(Trade trade);
    Task SaveChangesAsync();
}
