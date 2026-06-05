namespace Scada.Api.Services;

internal interface IIndustrialHeartbeatService
{
    void RegisterTag(int tagId, string tagName, string driverType, int? connectionId, int expectedIntervalMs);
    void UnregisterTag(int tagId);
    void RegisterConnection(string driverType, int connectionId);
    void RecordTag(TagValueEnvelope envelope);
    void RecordConnection(string driverType, int connectionId, DateTime receivedAt);
    void RecordError(string source, string message);
    IReadOnlyList<int> GetStaleTagIds();
    object GetSnapshot();
    IndustrialHeartbeatMetrics GetMetricsSnapshot();
}
