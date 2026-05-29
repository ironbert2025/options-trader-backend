using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface IScreenshotRepository
{
    Task<IEnumerable<Screenshot>> GetByTradeIdAsync(int tradeId);
    Task<int> CountByTradeIdAsync(int tradeId);
    Task AddAsync(Screenshot screenshot);
    Task SaveChangesAsync();
}
