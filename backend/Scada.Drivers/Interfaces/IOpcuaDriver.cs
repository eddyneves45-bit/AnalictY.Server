namespace Scada.Drivers.Interfaces;

public interface IOpcuaDriver : IDriver
{
    Task<string?> ReadNodeAsync(string nodeId);
    Task<bool> WriteNodeAsync(string nodeId, object value);
    Task SubscribeNodeAsync(string nodeId, Action<string, object> callback);
    Task UnsubscribeNodeAsync(string nodeId);
}
