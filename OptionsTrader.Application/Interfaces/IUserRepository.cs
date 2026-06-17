using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username);
}
