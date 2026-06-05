using System.Threading.Channels;

namespace Scada.Api.Services;

internal sealed class TagValueQueue : ITagValueQueue
{
    private readonly Channel<TagValueEnvelope> _channel = Channel.CreateBounded<TagValueEnvelope>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private int _approximateCount;
    private long _enqueuedCount;
    private long _dequeuedCount;
    private long _droppedCount;

    public int ApproximateCount => Math.Max(0, Volatile.Read(ref _approximateCount));
    public long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);
    public long DequeuedCount => Interlocked.Read(ref _dequeuedCount);
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public async ValueTask<bool> EnqueueAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            await _channel.Writer.WriteAsync(envelope, cancellationToken);
            Interlocked.Increment(ref _enqueuedCount);
            Interlocked.Increment(ref _approximateCount);
            return true;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }
    }

    public async ValueTask<TagValueEnvelope> DequeueAsync(CancellationToken cancellationToken)
    {
        var envelope = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Increment(ref _dequeuedCount);
        Interlocked.Decrement(ref _approximateCount);
        return envelope;
    }
}
