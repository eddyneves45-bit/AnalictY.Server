namespace Scada.Drivers.Interfaces;

public interface IMqttDriver : IDriver
{
    Task PublishAsync(string topic, string payload);
    Task SubscribeAsync(string topic, Action<string, string> callback);
    Task UnsubscribeAsync(string topic);
}
