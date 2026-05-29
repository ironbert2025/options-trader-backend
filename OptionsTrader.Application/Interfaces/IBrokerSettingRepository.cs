using OptionsTrader.Domain.Entities;

namespace OptionsTrader.Application.Interfaces;

public interface IBrokerSettingRepository
{
    Task<BrokerSetting?> GetActiveAsync();
}
