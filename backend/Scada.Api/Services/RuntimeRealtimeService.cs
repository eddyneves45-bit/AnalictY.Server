using Microsoft.AspNetCore.SignalR;
using Scada.Api.Realtime;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class RuntimeRealtimeService : IRuntimeRealtimeService
{
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IHubContext<MesHub> _hubContext;

    public RuntimeRealtimeService(
        ITagRuntimeService tagRuntimeService,
        IHubContext<MesHub> hubContext)
    {
        _tagRuntimeService = tagRuntimeService;
        _hubContext = hubContext;
    }

    public IReadOnlyList<object> BuildSnapshot()
    {
        return _tagRuntimeService.GetAllTagStates()
            .Values
            .Select(RuntimeService.NormalizeState)
            .ToList();
    }

    public async Task PublishAsync(int tagId, CancellationToken cancellationToken = default)
    {
        var state = _tagRuntimeService.GetTagState(tagId);
        if (state == null)
        {
            return;
        }

        await _hubContext.Clients.All.SendAsync(
            "runtime:update",
            RuntimeService.NormalizeState(state),
            cancellationToken);
    }
}
