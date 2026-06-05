using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class DriverStatusService : IDriverStatusService
{
    private readonly ScadaDbContext _dbContext;
    private readonly IOpcuaSessionFactory _sessionFactory;

    public DriverStatusService(ScadaDbContext dbContext, IOpcuaSessionFactory sessionFactory)
    {
        _dbContext = dbContext;
        _sessionFactory = sessionFactory;
    }

    public async Task<object> CheckOpcuaStatusAsync(string endpointUrl)
    {
        var config = new OpcuaConfig
        {
            ServerUrl = endpointUrl,
            SecurityMode = "None",
            SecurityPolicy = "None"
        };
        using var session = await _sessionFactory.CreateSessionAsync(config);
        return new { endpointUrl, isHealthy = session.Connected };
    }

    public async Task<ApplicationServiceResult> ConnectActiveOpcuaAsync(CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.OpcuaConfigs.FirstOrDefaultAsync(c => c.IsActive, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound(new { message = "Nenhuma configuracao OPC UA ativa encontrada" });
        }

        using var session = await _sessionFactory.CreateSessionAsync(config, cancellationToken);
        return ApplicationServiceResult.Ok(new
        {
            configId = config.Id,
            configName = config.Name,
            serverUrl = config.ServerUrl,
            isConnected = session.Connected,
            isHealthy = session.Connected
        });
    }

    public async Task<object> CheckMqttStatusAsync(string brokerUrl, string clientId)
    {
        var config = new Scada.Drivers.DTOs.MqttDriverConfig(brokerUrl, clientId);
        var driver = new Scada.Drivers.Adapters.MqttDriverAdapter(config);
        await driver.ConnectAsync();
        var isHealthy = await driver.IsHealthyAsync();
        await driver.DisconnectAsync();

        return new { brokerUrl, clientId, isHealthy };
    }

    public async Task<object> CheckModbusStatusAsync(string host, int port)
    {
        var config = new Scada.Drivers.DTOs.ModbusDriverConfig(host, port);
        var driver = new Scada.Drivers.Adapters.ModbusDriverAdapter(config);
        await driver.ConnectAsync();
        var isHealthy = await driver.IsHealthyAsync();
        await driver.DisconnectAsync();

        return new { host, port, isHealthy };
    }
}
