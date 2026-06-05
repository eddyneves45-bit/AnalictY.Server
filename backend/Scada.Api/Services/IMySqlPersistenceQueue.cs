namespace Scada.Api.Services;

internal interface IMySqlPersistenceQueue
{
    Task EnqueueAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
    Task<PendingMySqlQueueItem?> GetNextAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingMySqlQueueItem>> GetBatchAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(long id, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(long id, string error, CancellationToken cancellationToken = default);
    Task<int> CleanupProcessedAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken = default);
    Task<MySqlPersistenceHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default);
    void RecordWriteSuccess();
    void RecordWriteFailure(string error);
}

internal sealed record PendingMySqlQueueItem(long Id, TagValueEnvelope Envelope, int Attempts);
internal sealed record MySqlPersistenceHealthSnapshot(
    string Status,
    bool DatabaseReachable,
    int PendingCount,
    int FailedCount,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    string? LastError);
