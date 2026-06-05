using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class TagValueProcessorService : BackgroundService
{
    private readonly ITagValueQueue _queue;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly ITagRuntimeSnapshotStore _snapshotStore;
    private readonly IMySqlPersistenceQueue _mySqlPersistenceQueue;
    private readonly IAlertRuleEvaluator _alertRuleEvaluator;
    private readonly IIndustrialMetricsService _metricsService;
    private readonly IMachineRealtimeService _machineRealtimeService;
    private readonly IRuntimeRealtimeService _runtimeRealtimeService;
    private readonly IMesDashboardRealtimeService _mesDashboardRealtimeService;
    private readonly ILogger<TagValueProcessorService> _logger;

    public TagValueProcessorService(
        ITagValueQueue queue,
        ITagRuntimeService tagRuntimeService,
        IIndustrialHeartbeatService heartbeatService,
        ITagRuntimeSnapshotStore snapshotStore,
        IMySqlPersistenceQueue mySqlPersistenceQueue,
        IAlertRuleEvaluator alertRuleEvaluator,
        IIndustrialMetricsService metricsService,
        IMachineRealtimeService machineRealtimeService,
        IRuntimeRealtimeService runtimeRealtimeService,
        IMesDashboardRealtimeService mesDashboardRealtimeService,
        ILogger<TagValueProcessorService> logger)
    {
        _queue = queue;
        _tagRuntimeService = tagRuntimeService;
        _heartbeatService = heartbeatService;
        _snapshotStore = snapshotStore;
        _mySqlPersistenceQueue = mySqlPersistenceQueue;
        _alertRuleEvaluator = alertRuleEvaluator;
        _metricsService = metricsService;
        _machineRealtimeService = machineRealtimeService;
        _runtimeRealtimeService = runtimeRealtimeService;
        _mesDashboardRealtimeService = mesDashboardRealtimeService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var envelope = await _queue.DequeueAsync(stoppingToken);
                _tagRuntimeService.UpdateTagValue(envelope.TagId, envelope.Value, envelope.Quality);
                _tagRuntimeService.UpdateTagConnectionStatus(envelope.TagId, envelope.Quality == "GOOD");
                _heartbeatService.RecordTag(envelope);
                await _snapshotStore.PersistAsync(envelope, stoppingToken);
                if (!string.Equals(envelope.PersistenceMode, "telemetry", StringComparison.OrdinalIgnoreCase))
                {
                    await _mySqlPersistenceQueue.EnqueueAsync(envelope, stoppingToken);
                }
                await _alertRuleEvaluator.EvaluateAsync(envelope, stoppingToken);
                _metricsService.RecordProcessed(envelope);
                await _runtimeRealtimeService.PublishAsync(envelope.TagId, stoppingToken);
                await _machineRealtimeService.PublishFromTagAsync(envelope, stoppingToken);
                await _mesDashboardRealtimeService.PublishFromTagAsync(envelope, stoppingToken);

                if (envelope.ConnectionId.HasValue)
                {
                    _heartbeatService.RecordConnection(envelope.DriverType, envelope.ConnectionId.Value, envelope.ReceivedAt);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metricsService.RecordFailure();
                _heartbeatService.RecordError(nameof(TagValueProcessorService), ex.Message);
                _logger.LogError(ex, "Falha ao processar valor de TAG");
            }
        }
    }
}
