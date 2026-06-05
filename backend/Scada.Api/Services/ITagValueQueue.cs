namespace Scada.Api.Services;

internal interface ITagValueQueue
{
    int ApproximateCount { get; }
    long EnqueuedCount { get; }
    long DequeuedCount { get; }
    long DroppedCount { get; }
    ValueTask<bool> EnqueueAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
    ValueTask<TagValueEnvelope> DequeueAsync(CancellationToken cancellationToken);
}
