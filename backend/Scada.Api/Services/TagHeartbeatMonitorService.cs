using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class TagHeartbeatMonitorService : BackgroundService
{
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly ITagRuntimeService _tagRuntimeService;

    public TagHeartbeatMonitorService(
        IIndustrialHeartbeatService heartbeatService,
        ITagRuntimeService tagRuntimeService)
    {
        _heartbeatService = heartbeatService;
        _tagRuntimeService = tagRuntimeService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var tagId in _heartbeatService.GetStaleTagIds())
            {
                _tagRuntimeService.MarkTagAsStale(tagId);
            }
        }
    }
}
