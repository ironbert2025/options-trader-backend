using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface ITradeRepository
{
    Task<Trade?> GetByIdAsync(int id);
    Task<IEnumerable<Trade>> GetAllAsync();
    Task<bool> ExistsForDateAsync(DateOnly date);
    Task AddAsync(Trade trade);
    Task SaveChangesAsync();
}
