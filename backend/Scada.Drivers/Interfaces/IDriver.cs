namespace Scada.Drivers.Interfaces;

public interface IDriver
{
    string Name { get; }
    string Type { get; }
    bool IsConnected { get; }
    Task ConnectAsync();
    Task DisconnectAsync();
    Task<bool> IsHealthyAsync();
}
