using Scada.Core.Models.SQLite;

namespace Scada.Api.Services;

internal interface ITelegramNotificationService
{
    Task<object> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<object> ListConnectionsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertConnectionAsync(int? id, TelegramConnectionRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteConnectionAsync(int id, CancellationToken cancellationToken = default);
    Task<object> ListRecipientsAsync(int? connectionId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CaptureRecipientsAsync(int? connectionId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertRecipientAsync(int? id, TelegramRecipientRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteRecipientAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SendTestAsync(TelegramTestRequest? request = null, CancellationToken cancellationToken = default);
    Task SendAlertAsync(Alert alert, int? connectionId = null, IReadOnlyCollection<int>? recipientIds = null, CancellationToken cancellationToken = default);
}
