using Microsoft.Extensions.Logging;
using Scada.Drivers.OpcUa;
using Scada.Core.Models;

namespace Scada.Tests;

public class OpcUaConnectionTest
{
    private readonly ILoggerFactory _loggerFactory;

    public OpcUaConnectionTest()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Teste de conexão OPC-UA básico
    /// </summary>
    public async Task TestOpcUaBasicConnection()
    {
        var logger = _loggerFactory.CreateLogger<OpcUaConnectionTest>();
        logger.LogInformation("=== Iniciando teste OPC-UA básico ===");

        var config = new OpcUaConnectionConfig
        {
            EndpointUrl = "opc.tcp://DESKTOP-EDDY:4840/G01",
            ApplicationName = "SCADA OPC-UA Test Client",
            UseSecurity = false, // Desabilite para teste inicial
            Username = "", // Opcional
            Password = "", // Opcional
            AutoSubscribe = false
        };

        using var driver = new OpcUaDriver(config, _loggerFactory.CreateLogger<OpcUaDriver>());

        try
        {
            logger.LogInformation("Conectando ao servidor OPC-UA...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com sucesso!");

            // Ler um nó (exemplo: ns=2;s=Machine.Temperature)
            logger.LogInformation("Lendo tag de teste...");
            var tag = await driver.ReadTagAsync("ns=2;s=Machine.Temperature", DataType.Double);
            logger.LogInformation("Valor lido: {Value}, Qualidade: {Quality}", tag.Value, tag.Quality);

            // Escrever um valor
            logger.LogInformation("Escrevendo valor de teste...");
            await driver.WriteTagAsync("ns=2;s=Machine.Setpoint", 75.5, DataType.Double);

            // Ler múltiplas tags
            logger.LogInformation("Lendo múltiplas tags...");
            var tags = await driver.ReadMultipleTagsAsync(new[]
            {
                ("ns=2;s=Machine.Temperature", DataType.Double),
                ("ns=2;s=Machine.Pressure", DataType.Double),
                ("ns=2;s=Machine.Status", DataType.Bool)
            });

            foreach (var t in tags)
            {
                logger.LogInformation("Tag: {Address} = {Value}", t.Address, t.Value);
            }

            // Desconectar
            logger.LogInformation("Desconectando...");
            await driver.DisconnectAsync();
            logger.LogInformation("Teste concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste OPC-UA");
            throw;
        }
    }

    /// <summary>
    /// Teste de conexão OPC-UA com segurança
    /// </summary>
    public async Task TestOpcUaSecureConnection()
    {
        var logger = _loggerFactory.CreateLogger<OpcUaConnectionTest>();
        logger.LogInformation("=== Iniciando teste OPC-UA com segurança ===");

        var config = new OpcUaConnectionConfig
        {
            EndpointUrl = "opc.tcp://DESKTOP-EDDY:4840/G01",
            ApplicationName = "SCADA OPC-UA Secure Client",
            UseSecurity = true,
            Username = "admin", // Opcional
            Password = "password", // Opcional
            AutoSubscribe = false
        };

        using var driver = new OpcUaDriver(config, _loggerFactory.CreateLogger<OpcUaDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com segurança com sucesso!");

            // Testar leitura
            var tag = await driver.ReadTagAsync("ns=2;s=Machine.Temperature", DataType.Double);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste seguro concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste OPC-UA seguro");
            throw;
        }
    }

    /// <summary>
    /// Teste de subscrição OPC-UA
    /// </summary>
    public async Task TestOpcUaSubscription()
    {
        var logger = _loggerFactory.CreateLogger<OpcUaConnectionTest>();
        logger.LogInformation("=== Testando subscrição OPC-UA ===");

        var config = new OpcUaConnectionConfig
        {
            EndpointUrl = "opc.tcp://DESKTOP-EDDY:4840/G01",
            ApplicationName = "SCADA OPC-UA Subscription Test",
            UseSecurity = false,
            AutoSubscribe = true,
            PublishingInterval = 1000,
            SamplingInterval = 500
        };

        using var driver = new OpcUaDriver(config, _loggerFactory.CreateLogger<OpcUaDriver>());

        driver.TagValueChanged += (sender, e) =>
        {
            logger.LogInformation("Tag alterada: {Address} = {Value} às {Timestamp}", 
                e.Address, e.Value, e.Timestamp);
        };

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado, iniciando subscrição...");

            // Subscrever a tags
            await driver.SubscribeToTagAsync("ns=2;s=Machine.Temperature", DataType.Double);
            await driver.SubscribeToTagAsync("ns=2;s=Machine.Pressure", DataType.Double);

            // Aguardar mudanças por 30 segundos
            logger.LogInformation("Aguardando mudanças por 30 segundos...");
            await Task.Delay(30000);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste de subscrição concluído!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste de subscrição");
            throw;
        }
    }

    /// <summary>
    /// Teste com servidor público OPC-UA (FreeOpcUa)
    /// </summary>
    public async Task TestPublicOpcUaServer()
    {
        var logger = _loggerFactory.CreateLogger<OpcUaConnectionTest>();
        logger.LogInformation("=== Testando servidor público OPC-UA ===");

        var config = new OpcUaConnectionConfig
        {
            EndpointUrl = "opc.tcp://opcuaserver.com:4840", // Servidor público de teste
            ApplicationName = "SCADA Public Server Test",
            UseSecurity = false,
            AutoSubscribe = false
        };

        using var driver = new OpcUaDriver(config, _loggerFactory.CreateLogger<OpcUaDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado ao servidor público!");

            // Ler nó raiz
            var tag = await driver.ReadTagAsync("i=84", DataType.String); // Root Folder
            logger.LogInformation("Root Folder: {Value}", tag.Value);

            await driver.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro na conexão com servidor público");
            throw;
        }
    }
}
