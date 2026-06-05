namespace Scada.Api.Services;

internal sealed class VirtualMachineSimulationWorker : BackgroundService
{
    private readonly IVirtualMachineRuntimeService _runtimeService;
    private readonly ITagValueQueue _tagValueQueue;
    private readonly ILogger<VirtualMachineSimulationWorker> _logger;

    public VirtualMachineSimulationWorker(
        IVirtualMachineRuntimeService runtimeService,
        ITagValueQueue tagValueQueue,
        ILogger<VirtualMachineSimulationWorker> logger)
    {
        _runtimeService = runtimeService;
        _tagValueQueue = tagValueQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var state in _runtimeService.GetRunningStates())
            {
                try
                {
                    if (!state.Advance(DateTime.UtcNow, out var publish))
                    {
                        continue;
                    }

                    await PublishAsync(publish.Tags["production_counter"], publish.ProductionCounter, stoppingToken);
                    await PublishAsync(publish.Tags["machine_status"], publish.Status, stoppingToken);
                    await PublishAsync(publish.Tags["downtime_reason_code"], publish.DowntimeReasonCode, stoppingToken);
                    await PublishAsync(publish.Tags["loss_count"], publish.LossCounter, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao avançar máquina virtual {MachineId}", state.MachineId);
                }
            }
        }
    }

    private ValueTask<bool> PublishAsync(VirtualMachineRuntimeTag tag, object value, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _tagValueQueue.EnqueueAsync(new TagValueEnvelope(
            tag.Id,
            tag.Name,
            tag.DriverType,
            tag.PersistenceMode,
            null,
            value,
            "GOOD",
            now,
            now,
            "virtual-worker"),
            cancellationToken);
    }
}
