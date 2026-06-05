namespace Scada.Api.Services;

internal interface IOpcuaConfigService
{
    Task<object> GetConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertConfigAsync(OpcuaConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> BrowseAsync(string? nodeId, int? connectionId, CancellationToken cancellationToken = default);
}
