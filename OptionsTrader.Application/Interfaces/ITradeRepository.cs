using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface ITradeRepository
{
    Task<Trade?> GetByIdAsync(int id);
    Task<IEnumerable<Trade>> GetAllAsync(int userId);
    Task<IEnumerable<Trade>> GetByDateAsync(DateOnly date, int userId);
    Task<IEnumerable<Trade>> GetByMonthAsync(int year, int month, int userId);
    Task<bool> ExistsForDateAsync(DateOnly date, int userId);
    Task<int> NextDailyTradeNumberAsync(DateOnly date, int userId);
    Task AddAsync(Trade trade);
    Task UpdateAsync(Trade trade);
    Task SaveChangesAsync();
}
