using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Realtime;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MesDashboardRealtimeService : IMesDashboardRealtimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<MesHub> _hubContext;

    public MesDashboardRealtimeService(
        IServiceScopeFactory scopeFactory,
        IHubContext<MesHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    public async Task<IReadOnlyList<MesDashboardMachineState>> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var machines = await dbContext.Machines
            .AsNoTracking()
            .Where(machine => machine.IsActive)
            .ToListAsync(cancellationToken);
        var states = await dbContext.MachineStates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return machines.Select(machine => Build(machine.Code, states.FirstOrDefault(state => state.MachineId == machine.Id.ToString()))).ToList();
    }

    public async Task PublishFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var machines = await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map =>
                map.IsActive &&
                map.TagConfigId == envelope.TagId &&
                map.TagAlias == "machine_status")
            .Join(
                dbContext.Machines.AsNoTracking(),
                map => map.MachineId,
                machine => machine.Id.ToString(),
                (_, machine) => machine)
            .ToListAsync(cancellationToken);

        foreach (var machine in machines)
        {
            var state = await dbContext.MachineStates
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.MachineId == machine.Id.ToString(), cancellationToken);
            await _hubContext.Clients.All.SendAsync("mes:update", Build(machine.Code, state), cancellationToken);
        }
    }

    private static MesDashboardMachineState Build(string machineCode, Scada.Core.Models.SQLite.MachineState? state)
    {
        return new MesDashboardMachineState(
            machineCode,
            state?.State ?? "OFFLINE",
            state?.Context ?? "NONE",
            new
            {
                running_time = state?.State == "RUNNING" ? (int)(DateTime.UtcNow - state.StateStartTime).TotalSeconds : 0,
                stopped_time = state?.State == "STOPPED" ? (int)(DateTime.UtcNow - state.StateStartTime).TotalSeconds : 0
            },
            null);
    }
}
