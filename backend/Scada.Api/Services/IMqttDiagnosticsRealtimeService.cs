namespace Scada.Api.Services;

internal interface IMqttDiagnosticsRealtimeService
{
    Task<object> BuildSnapshotAsync(int connectionId, CancellationToken cancellationToken = default);
    IReadOnlyList<object> BuildMessageSnapshot(int connectionId);
    Task PublishAsync(int connectionId, CancellationToken cancellationToken = default);
    Task PublishMessageAsync(MqttRuntimeMessage message, CancellationToken cancellationToken = default);
}
