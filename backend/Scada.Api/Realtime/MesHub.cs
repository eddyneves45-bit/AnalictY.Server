using Microsoft.AspNetCore.SignalR;
using Scada.Api.Services;

namespace Scada.Api.Realtime;

internal sealed class MesHub : Hub
{
    private readonly IMachineRealtimeService _machineRealtimeService;
    private readonly IRuntimeRealtimeService _runtimeRealtimeService;
    private readonly IAlertRealtimeService _alertRealtimeService;
    private readonly IMesDashboardRealtimeService _mesDashboardRealtimeService;
    private readonly IMqttDiagnosticsRealtimeService _mqttDiagnosticsRealtimeService;

    public MesHub(
        IMachineRealtimeService machineRealtimeService,
        IRuntimeRealtimeService runtimeRealtimeService,
        IAlertRealtimeService alertRealtimeService,
        IMesDashboardRealtimeService mesDashboardRealtimeService,
        IMqttDiagnosticsRealtimeService mqttDiagnosticsRealtimeService)
    {
        _machineRealtimeService = machineRealtimeService;
        _runtimeRealtimeService = runtimeRealtimeService;
        _alertRealtimeService = alertRealtimeService;
        _mesDashboardRealtimeService = mesDashboardRealtimeService;
        _mqttDiagnosticsRealtimeService = mqttDiagnosticsRealtimeService;
    }

    public override async Task OnConnectedAsync()
    {
        var cancellationToken = Context.ConnectionAborted;

        await Clients.Caller.SendAsync("machines:snapshot", await _machineRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
        await Clients.Caller.SendAsync("runtime:snapshot", _runtimeRealtimeService.BuildSnapshot());
        await Clients.Caller.SendAsync("alerts:snapshot", await _alertRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
        await Clients.Caller.SendAsync("mes:snapshot", await _mesDashboardRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
        await base.OnConnectedAsync();
    }

    public async Task RequestMachineSnapshot()
    {
        var cancellationToken = Context.ConnectionAborted;
        await Clients.Caller.SendAsync("machines:snapshot", await _machineRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
    }

    public Task RequestRuntimeSnapshot()
    {
        return Clients.Caller.SendAsync("runtime:snapshot", _runtimeRealtimeService.BuildSnapshot());
    }

    public async Task RequestAlertSnapshot()
    {
        var cancellationToken = Context.ConnectionAborted;
        await Clients.Caller.SendAsync("alerts:snapshot", await _alertRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
    }

    public async Task RequestMesSnapshot()
    {
        var cancellationToken = Context.ConnectionAborted;
        await Clients.Caller.SendAsync("mes:snapshot", await _mesDashboardRealtimeService.BuildSnapshotAsync(cancellationToken), cancellationToken);
    }

    public async Task SubscribeMqttDiagnostics(int connectionId)
    {
        var cancellationToken = Context.ConnectionAborted;
        await Groups.AddToGroupAsync(Context.ConnectionId, MqttGroup(connectionId));
        await Clients.Caller.SendAsync(
            "mqtt:diagnostics",
            await _mqttDiagnosticsRealtimeService.BuildSnapshotAsync(connectionId, cancellationToken),
            cancellationToken);
        await Clients.Caller.SendAsync(
            "mqtt:messages:snapshot",
            _mqttDiagnosticsRealtimeService.BuildMessageSnapshot(connectionId),
            cancellationToken);
    }

    public Task UnsubscribeMqttDiagnostics(int connectionId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, MqttGroup(connectionId));
    }

    internal static string MqttGroup(int connectionId) => $"mqtt:{connectionId}";
}
