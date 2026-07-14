using Microsoft.EntityFrameworkCore;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<BrokerSetting> BrokerSettings => Set<BrokerSetting>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Trade>(e =>
        {
            e.Property(t => t.StrikePrice).HasPrecision(18, 4);
            e.Property(t => t.SpotPrice).HasPrecision(18, 4);
            e.Property(t => t.EntryPrice).HasPrecision(18, 4);
            e.Property(t => t.ExitPrice).HasPrecision(18, 4);
            e.Property(t => t.TargetPercent).HasPrecision(18, 4);
            e.Property(t => t.Pnl).HasPrecision(18, 4);
            e.Property(t => t.PnlPercent).HasPrecision(18, 4);

            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        // Seed 5 fixed users with bcrypt-hashed passwords
        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Username = "user1", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass1234!") },
            new User { Id = 2, Username = "user2", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass1234!") },
            new User { Id = 3, Username = "user3", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass1234!") },
            new User { Id = 4, Username = "user4", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass1234!") },
            new User { Id = 5, Username = "user5", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass1234!") }
        );
    }
}
