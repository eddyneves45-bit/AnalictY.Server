namespace Scada.Api.Services;

internal sealed class IndustrialMetricsService : IIndustrialMetricsService
{
    private long _processedMessages;
    private long _failedMessages;
    private long _lastProcessingDelayTicks;
    private long _maxProcessingDelayTicks;

    public void RecordProcessed(TagValueEnvelope envelope)
    {
        Interlocked.Increment(ref _processedMessages);

        var delay = DateTime.UtcNow - envelope.ReceivedAt;
        var delayTicks = Math.Max(0, delay.Ticks);
        Interlocked.Exchange(ref _lastProcessingDelayTicks, delayTicks);

        long observed;
        do
        {
            observed = Interlocked.Read(ref _maxProcessingDelayTicks);
            if (delayTicks <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxProcessingDelayTicks, delayTicks, observed) != observed);
    }

    public void RecordFailure()
    {
        Interlocked.Increment(ref _failedMessages);
    }

    public IndustrialMetricsSnapshot GetSnapshot()
    {
        return new IndustrialMetricsSnapshot(
            Interlocked.Read(ref _processedMessages),
            Interlocked.Read(ref _failedMessages),
            TimeSpan.FromTicks(Interlocked.Read(ref _lastProcessingDelayTicks)).TotalSeconds,
            TimeSpan.FromTicks(Interlocked.Read(ref _maxProcessingDelayTicks)).TotalSeconds);
    }
}
