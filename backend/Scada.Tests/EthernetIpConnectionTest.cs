using Microsoft.Extensions.Logging;
using Scada.Drivers.EthernetIp;
using Scada.Core.Models;

namespace Scada.Tests;

public class EthernetIpConnectionTest
{
    private readonly ILoggerFactory _loggerFactory;

    public EthernetIpConnectionTest()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Teste de conexão Ethernet/IP básico
    /// </summary>
    public async Task TestEthernetIpBasicConnection()
    {
        var logger = _loggerFactory.CreateLogger<EthernetIpConnectionTest>();
        logger.LogInformation("=== Iniciando teste Ethernet/IP básico ===");

        var config = new EthernetIpConnectionConfig
        {
            Host = "192.168.1.1", // Altere para seu PLC Allen-Bradley
            Port = 44818,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new EthernetIpDriver(config, _loggerFactory.CreateLogger<EthernetIpDriver>());

        try
        {
            logger.LogInformation("Conectando ao dispositivo Ethernet/IP...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com sucesso!");

            // Ler tag (formato: class:instance:attribute)
            logger.LogInformation("Lendo tag de identidade...");
            var tag = await driver.ReadTagAsync("0x67:1:1", DataType.String);
            logger.LogInformation("Vendor: {Value}", tag.Value);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste Ethernet/IP básico concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste Ethernet/IP básico");
            throw;
        }
    }

    /// <summary>
    /// Teste de leitura/escrita Ethernet/IP
    /// </summary>
    public async Task TestEthernetIpReadWrite()
    {
        var logger = _loggerFactory.CreateLogger<EthernetIpConnectionTest>();
        logger.LogInformation("=== Testando leitura/escrita Ethernet/IP ===");

        var config = new EthernetIpConnectionConfig
        {
            Host = "192.168.1.1",
            Port = 44818,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new EthernetIpDriver(config, _loggerFactory.CreateLogger<EthernetIpDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado!");

            // Escrever valor
            logger.LogInformation("Escrevendo valor...");
            await driver.WriteTagAsync("0x67:1:1", 100, DataType.Int16);

            // Ler valor
            logger.LogInformation("Lendo valor...");
            var tag = await driver.ReadTagAsync("0x67:1:1", DataType.Int16);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            // Testar diferentes tipos de dados
            await driver.WriteTagAsync("0x67:1:2", 75.5, DataType.Float);
            var floatTag = await driver.ReadTagAsync("0x67:1:2", DataType.Float);
            logger.LogInformation("Float: {Value}", floatTag.Value);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste leitura/escrita concluído!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste leitura/escrita");
            throw;
        }
    }

    /// <summary>
    /// Teste de polling automático
    /// </summary>
    public async Task TestEthernetIpPolling()
    {
        var logger = _loggerFactory.CreateLogger<EthernetIpConnectionTest>();
        logger.LogInformation("=== Testando polling Ethernet/IP ===");

        var config = new EthernetIpConnectionConfig
        {
            Host = "192.168.1.1",
            Port = 44818,
            TimeoutMs = 5000,
            AutoPoll = true,
            PollIntervalMs = 1000
        };

        using var driver = new EthernetIpDriver(config, _loggerFactory.CreateLogger<EthernetIpDriver>());

        driver.TagValueChanged += (sender, e) =>
        {
            logger.LogInformation("Tag alterada: {Address} = {Value} às {Timestamp}", 
                e.Address, e.Value, e.Timestamp);
        };

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado, registrando tags para polling...");

            // Registrar tags para polling
            driver.RegisterTagForPolling("0x67:1:1", DataType.Int16);
            driver.RegisterTagForPolling("0x67:1:2", DataType.Float);
            driver.RegisterTagForPolling("0x67:1:3", DataType.Bool);

            // Aguardar polling por 30 segundos
            logger.LogInformation("Aguardando polling por 30 segundos...");
            await Task.Delay(30000);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste de polling concluído!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste de polling");
            throw;
        }
    }

    /// <summary>
    /// Teste com PLC Rockwell/Allen-Bradley
    /// </summary>
    public async Task TestAllenBradleyPlc()
    {
        var logger = _loggerFactory.CreateLogger<EthernetIpConnectionTest>();
        logger.LogInformation("=== Testando PLC Allen-Bradley ===");

        var config = new EthernetIpConnectionConfig
        {
            Host = "192.168.1.10", // IP do seu PLC
            Port = 44818,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new EthernetIpDriver(config, _loggerFactory.CreateLogger<EthernetIpDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado ao PLC Allen-Bradley!");

            // Ler tag do ControlLogix
            // Nota: Em produção, use biblioteca especializada como CIPSharp
            var tag = await driver.ReadTagAsync("0x67:1:1", DataType.String);
            logger.LogInformation("Tag lida: {Value}", tag.Value);

            await driver.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste com PLC Allen-Bradley");
            throw;
        }
    }

    /// <summary>
    /// Teste de múltiplas conexões
    /// </summary>
    public async Task TestMultipleConnections()
    {
        var logger = _loggerFactory.CreateLogger<EthernetIpConnectionTest>();
        logger.LogInformation("=== Testando múltiplas conexões ===");

        var configs = new[]
        {
            new EthernetIpConnectionConfig { Host = "192.168.1.1", Port = 44818 },
            new EthernetIpConnectionConfig { Host = "192.168.1.2", Port = 44818 }
        };

        var drivers = configs.Select(c => new EthernetIpDriver(c, _loggerFactory.CreateLogger<EthernetIpDriver>())).ToList();

        try
        {
            // Conectar a todos
            var connectTasks = drivers.Select(d => d.ConnectAsync());
            await Task.WhenAll(connectTasks);
            logger.LogInformation("Conectado a {Count} dispositivos", drivers.Count);

            // Ler de todos
            var readTasks = drivers.Select(d => d.ReadTagAsync("0x67:1:1", DataType.String));
            var results = await Task.WhenAll(readTasks);

            foreach (var result in results)
            {
                logger.LogInformation("Valor: {Value}", result.Value);
            }

            // Desconectar todos
            var disconnectTasks = drivers.Select(d => d.DisconnectAsync());
            await Task.WhenAll(disconnectTasks);
            logger.LogInformation("Todas conexões fechadas");
        }
        finally
        {
            foreach (var driver in drivers)
            {
                driver.Dispose();
            }
        }
    }
}
