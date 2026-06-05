namespace Scada.Api.Services;

internal sealed class MySqlPersistenceWorker : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProcessedRetention = TimeSpan.FromHours(6);
    private const int BatchSize = 100;

    private readonly IMySqlPersistenceQueue _queue;
    private readonly ITagHistoryStore _historyStore;
    private readonly IMesEventStore _mesEventStore;
    private readonly IMachineRealtimeService _machineRealtimeService;
    private readonly ILogger<MySqlPersistenceWorker> _logger;
    private DateTime _nextCleanupAt = DateTime.UtcNow.Add(CleanupInterval);

    public MySqlPersistenceWorker(
        IMySqlPersistenceQueue queue,
        ITagHistoryStore historyStore,
        IMesEventStore mesEventStore,
        IMachineRealtimeService machineRealtimeService,
        ILogger<MySqlPersistenceWorker> logger)
    {
        _queue = queue;
        _historyStore = historyStore;
        _mesEventStore = mesEventStore;
        _machineRealtimeService = machineRealtimeService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupIfDueAsync(stoppingToken);

            var items = await _queue.GetBatchAsync(BatchSize, stoppingToken);
            if (items.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            foreach (var item in items)
            {
                try
                {
                    await _historyStore.PersistIfChangedAsync(item.Envelope, stoppingToken);
                    await _mesEventStore.ProcessAsync(item.Envelope, stoppingToken);
                    await _machineRealtimeService.PublishEffectiveFromTagAsync(item.Envelope, stoppingToken);
                    await _queue.MarkProcessedAsync(item.Id, stoppingToken);
                    _queue.RecordWriteSuccess();
                }
                catch (Exception ex)
                {
                    _queue.RecordWriteFailure(ex.Message);
                    await _queue.MarkFailedAsync(item.Id, ex.Message, stoppingToken);
                    _logger.LogWarning(ex, "Falha ao persistir envelope MES no MySQL; nova tentativa agendada");
                }
            }
        }
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now < _nextCleanupAt)
        {
            return;
        }

        _nextCleanupAt = now.Add(CleanupInterval);
        var removed = await _queue.CleanupProcessedAsync(ProcessedRetention, 5_000, cancellationToken);
        if (removed > 0)
        {
            _logger.LogInformation("Limpeza da fila MySQL removeu {Count} envelope(s) processado(s).", removed);
        }
    }
}
