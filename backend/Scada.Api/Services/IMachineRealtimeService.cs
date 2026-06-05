namespace Scada.Api.Services;

internal interface IMachineRealtimeService
{
    Task<IReadOnlyList<MachineRealtimeState>> BuildSnapshotAsync(CancellationToken cancellationToken = default);
    Task PublishFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
    Task PublishEffectiveFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
}

internal sealed record MachineRealtimeState(int machine_id, object resolved_state);
