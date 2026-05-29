using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence.Repositories;

public class BrokerSettingRepository(AppDbContext db) : IBrokerSettingRepository
{
    public Task<BrokerSetting?> GetActiveAsync() =>
        db.BrokerSettings.FirstOrDefaultAsync(b => b.IsActive);
}
