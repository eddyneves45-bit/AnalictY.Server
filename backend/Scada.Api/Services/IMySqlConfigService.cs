namespace Scada.Api.Services;

internal interface IMySqlConfigService
{
    Task<object> GetConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertConfigAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetPrimaryConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetLocalConfigAsync(int id, bool isLocal, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestRequestAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> InitConfigAsync(int id, CancellationToken cancellationToken = default);
}
