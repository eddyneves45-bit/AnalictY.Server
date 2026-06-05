namespace Scada.Api.Services;

internal interface IMqttRuntimeMonitor
{
    void RegisterSubscription(int connectionId, string topic);
    void UnregisterSubscription(int connectionId, string topic);
    void RecordConnected(int connectionId);
    void RecordConnectionFailure(int connectionId, string message);
    MqttRuntimeMessage AddMessage(int connectionId, string topic, string payload, int qos, bool retain);
    IReadOnlyList<MqttRuntimeMessage> GetMessages(int? connectionId = null);
    IReadOnlyList<string> GetSubscribedTopics(int? connectionId = null);
    IReadOnlyList<string> GetDiscoveredTopics(int? connectionId = null);
    object? GetLatestValue(string tagAddress, string dataType);
    object GetDiagnostics(int? connectionId, string broker, int port, string clientId, bool connected);
}

internal sealed record MqttRuntimeMessage(
    int ConnectionId,
    string Topic,
    string Payload,
    int Qos,
    bool Retain,
    DateTime Timestamp);
