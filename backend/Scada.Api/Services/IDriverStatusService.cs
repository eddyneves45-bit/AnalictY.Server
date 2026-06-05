namespace Scada.Api.Services;

internal interface IDriverStatusService
{
    Task<object> CheckOpcuaStatusAsync(string endpointUrl);
    Task<ApplicationServiceResult> ConnectActiveOpcuaAsync(CancellationToken cancellationToken = default);
    Task<object> CheckMqttStatusAsync(string brokerUrl, string clientId);
    Task<object> CheckModbusStatusAsync(string host, int port);
}
