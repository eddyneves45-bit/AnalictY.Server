namespace Scada.Api.Services;

internal interface IMqttConfigService
{
    Task<object> GetConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertConfigAsync(MqttConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestConfigAsync(int id, CancellationToken cancellationToken = default);
}
