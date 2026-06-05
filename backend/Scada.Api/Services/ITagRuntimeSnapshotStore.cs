namespace Scada.Api.Services;

internal interface ITagRuntimeSnapshotStore
{
    Task PersistAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, RestoredTagRuntimeSnapshot>> LoadAsync(CancellationToken cancellationToken = default);
}

internal sealed record RestoredTagRuntimeSnapshot(
    int TagId,
    object? Value,
    DateTime SourceTimestamp);
