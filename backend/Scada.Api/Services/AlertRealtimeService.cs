using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Realtime;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class AlertRealtimeService : IAlertRealtimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<MesHub> _hubContext;

    public AlertRealtimeService(
        IServiceScopeFactory scopeFactory,
        IHubContext<MesHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    public async Task<IReadOnlyList<Alert>> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        return await dbContext.Alerts
            .AsNoTracking()
            .OrderByDescending(alert => alert.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public Task PublishCreatedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync("alerts:created", alert, cancellationToken);
    }

    public Task PublishUpdatedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync("alerts:updated", alert, cancellationToken);
    }

    public Task PublishDeletedAsync(int alertId, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync("alerts:deleted", alertId, cancellationToken);
    }
}
