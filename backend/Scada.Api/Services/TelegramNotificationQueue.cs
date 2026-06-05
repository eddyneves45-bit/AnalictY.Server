using System.Threading.Channels;

namespace Scada.Api.Services;

internal sealed class TelegramNotificationQueue : ITelegramNotificationQueue
{
    private readonly Channel<TelegramNotificationMessage> _channel = Channel.CreateBounded<TelegramNotificationMessage>(
        new BoundedChannelOptions(5_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private long _droppedCount;

    public ValueTask<bool> EnqueueAsync(TelegramNotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return ValueTask.FromResult(true);
        }

        Interlocked.Increment(ref _droppedCount);
        return ValueTask.FromResult(false);
    }

    public IAsyncEnumerable<TelegramNotificationMessage> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
