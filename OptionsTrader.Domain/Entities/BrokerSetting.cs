using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Domain.Entities;

public class BrokerSetting
{
    public int Id { get; set; }
    public BrokerName BrokerName { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
