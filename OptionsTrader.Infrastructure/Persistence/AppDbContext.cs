using Microsoft.EntityFrameworkCore;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<BrokerSetting> BrokerSettings => Set<BrokerSetting>();
}
