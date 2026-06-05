using Scada.Core.Models.SQLite;

namespace Scada.Api.Services;

internal interface IAlertRealtimeService
{
    Task<IReadOnlyList<Alert>> BuildSnapshotAsync(CancellationToken cancellationToken = default);
    Task PublishCreatedAsync(Alert alert, CancellationToken cancellationToken = default);
    Task PublishUpdatedAsync(Alert alert, CancellationToken cancellationToken = default);
    Task PublishDeletedAsync(int alertId, CancellationToken cancellationToken = default);
}
