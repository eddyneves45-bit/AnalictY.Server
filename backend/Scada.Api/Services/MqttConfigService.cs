using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class MqttConfigService : IMqttConfigService
{
    private readonly ScadaDbContext _dbContext;

    public MqttConfigService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.MqttConfigs.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> UpsertConfigAsync(MqttConfigRequest request, CancellationToken cancellationToken = default)
    {
        if (request.broker_port == 8000)
        {
            return ApplicationServiceResult.BadRequest(new { error = "A porta 8000 é da API HTTP. Use a porta real do broker MQTT, como 1883 ou 8883 com TLS." });
        }

        if (request.tls_enabled && request.broker_port == 1883)
        {
            return ApplicationServiceResult.BadRequest(new { error = "MQTT com TLS normalmente usa a porta 8883. Ajuste a porta ou desative TLS." });
        }

        var config = request.id.HasValue
            ? await _dbContext.MqttConfigs.FindAsync(new object[] { request.id.Value }, cancellationToken)
            : null;

        if (config == null)
        {
            config = new MqttConfig { CreatedAt = DateTime.UtcNow };
            _dbContext.MqttConfigs.Add(config);
        }

        config.Name = request.name;
        config.ClientId = request.client_id;
        config.BrokerHost = request.broker_host;
        config.BrokerPort = request.broker_port;
        config.Username = request.username;
        config.Password = request.password;
        config.TlsEnabled = request.tls_enabled;
        config.CaCertPath = request.ca_cert_path;
        config.ClientCertPath = request.client_cert_path;
        config.ClientKeyPath = request.client_key_path;
        config.Topics = request.topics;
        config.Qos = request.qos;
        config.IsActive = request.is_active ?? true;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(config);
    }

    public async Task<ApplicationServiceResult> TestConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MqttConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound(new { message = "Configuração MQTT não encontrada" });
        }

        var driverConfig = new Scada.Drivers.DTOs.MqttDriverConfig(
            config.BrokerHost,
            string.IsNullOrWhiteSpace(config.ClientId) ? $"scada-test-{Guid.NewGuid():N}" : config.ClientId,
            config.Username,
            config.Password,
            config.TlsEnabled,
            config.BrokerPort,
            config.CaCertPath,
            config.ClientCertPath,
            config.ClientKeyPath,
            config.Qos);

        var driver = new Scada.Drivers.Adapters.MqttDriverAdapter(driverConfig);

        try
        {
            await driver.ConnectAsync();
            var isHealthy = await driver.IsHealthyAsync();

            return ApplicationServiceResult.Ok(new
            {
                configId = config.Id,
                configName = config.Name,
                broker = $"{config.BrokerHost}:{config.BrokerPort}",
                tlsEnabled = config.TlsEnabled,
                isConnected = driver.IsConnected,
                isHealthy
            });
        }
        catch (Exception ex)
        {
            return ApplicationServiceResult.BadRequest(new
            {
                configId = config.Id,
                configName = config.Name,
                broker = $"{config.BrokerHost}:{config.BrokerPort}",
                tlsEnabled = config.TlsEnabled,
                isConnected = false,
                error = ex.Message
            });
        }
        finally
        {
            await driver.DisconnectAsync();
        }
    }

    public async Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MqttConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.MqttConfigs.Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Configuração MQTT excluída" });
    }
}
