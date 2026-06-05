namespace Scada.Api.Services;

internal interface IDowntimeService
{
    Task<object> ListAsync(string? machineId, DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken = default);
    Task<object> ListReasonsAsync(CancellationToken cancellationToken = default);
    Task<object> CreateReasonAsync(DowntimeReasonCreateRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> ClassifyAsync(long eventId, DowntimeClassifyRequest request, CancellationToken cancellationToken = default);
    Task<object> GetRetentionAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetRetentionAsync(DowntimeRetentionRequest request, CancellationToken cancellationToken = default);
}
