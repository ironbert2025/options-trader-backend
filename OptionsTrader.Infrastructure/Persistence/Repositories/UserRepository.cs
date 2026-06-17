using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Infrastructure.Persistence.Repositories;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> FindByUsernameAsync(string username)
        => context.Users.FirstOrDefaultAsync(u => u.Username == username);
}
