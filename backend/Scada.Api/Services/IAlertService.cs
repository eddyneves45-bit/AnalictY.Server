namespace Scada.Api.Services;

internal interface IAlertService
{
    Task<object> ListAlertsAsync(
        string? machineId,
        string? alertType,
        string? severity,
        bool? isAcknowledged,
        int limit,
        CancellationToken cancellationToken = default);

    Task<object> CreateAlertAsync(AlertCreateRequest request, CancellationToken cancellationToken = default);
    Task<object> GetRetentionAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetRetentionAsync(AlertRetentionRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> AcknowledgeAlertAsync(int id, string acknowledgedBy, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteAlertAsync(int id, CancellationToken cancellationToken = default);
}
