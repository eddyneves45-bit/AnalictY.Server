namespace Scada.Api.Services;

internal interface IMesDashboardRealtimeService
{
    Task<IReadOnlyList<MesDashboardMachineState>> BuildSnapshotAsync(CancellationToken cancellationToken = default);
    Task PublishFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
}

internal sealed record MesDashboardMachineState(
    string tag,
    string state,
    string reason,
    object metrics,
    object? oee);
