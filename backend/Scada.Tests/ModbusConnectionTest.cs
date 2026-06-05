using Microsoft.Extensions.Logging;
using Scada.Drivers.Modbus;
using Scada.Core.Models;
using System.IO.Ports;

namespace Scada.Tests;

public class ModbusConnectionTest
{
    private readonly ILoggerFactory _loggerFactory;

    public ModbusConnectionTest()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Teste de conexão Modbus TCP
    /// </summary>
    public async Task TestModbusTcpConnection()
    {
        var logger = _loggerFactory.CreateLogger<ModbusConnectionTest>();
        logger.LogInformation("=== Iniciando teste Modbus TCP ===");

        var config = new ModbusConnectionConfig
        {
            IsTcp = true,
            Host = "localhost", // Altere para seu dispositivo Modbus TCP
            Port = 502,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new ModbusDriver(config, _loggerFactory.CreateLogger<ModbusDriver>());

        try
        {
            logger.LogInformation("Conectando ao dispositivo Modbus TCP...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com sucesso!");

            // Ler holding register (formato: slaveId:address:function)
            logger.LogInformation("Lendo holding register...");
            var tag = await driver.ReadTagAsync("1:100:03", DataType.Int16);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            // Escrever valor
            logger.LogInformation("Escrevendo valor...");
            await driver.WriteTagAsync("1:100:03", 123, DataType.Int16);

            // Ler múltiplos registros
            logger.LogInformation("Lendo múltiplos registros...");
            var tags = await driver.ReadMultipleTagsAsync(new[]
            {
                ("1:100:03", DataType.Int16),
                ("1:101:03", DataType.Int16),
                ("1:102:03", DataType.Int16)
            });

            foreach (var t in tags)
            {
                logger.LogInformation("Tag: {Address} = {Value}", t.Address, t.Value);
            }

            // Ler float (2 registros)
            logger.LogInformation("Lendo valor float...");
            var floatTag = await driver.ReadTagAsync("1:200:03", DataType.Float);
            logger.LogInformation("Float: {Value}", floatTag.Value);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste Modbus TCP concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste Modbus TCP");
            throw;
        }
    }

    /// <summary>
    /// Teste de conexão Modbus RTU (porta serial)
    /// </summary>
    public async Task TestModbusRtuConnection()
    {
        var logger = _loggerFactory.CreateLogger<ModbusConnectionTest>();
        logger.LogInformation("=== Iniciando teste Modbus RTU ===");

        var config = new ModbusConnectionConfig
        {
            IsTcp = false,
            PortName = "COM1", // Altere para sua porta serial
            BaudRate = 9600,
            Parity = Parity.None,
            DataBits = 8,
            StopBits = StopBits.One,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new ModbusDriver(config, _loggerFactory.CreateLogger<ModbusDriver>());

        try
        {
            logger.LogInformation("Conectando ao dispositivo Modbus RTU...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com sucesso!");

            // Ler holding register
            var tag = await driver.ReadTagAsync("1:100:03", DataType.Int16);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            // Escrever valor
            await driver.WriteTagAsync("1:100:03", 456, DataType.Int16);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste Modbus RTU concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste Modbus RTU");
            throw;
        }
    }

    /// <summary>
    /// Teste de polling automático
    /// </summary>
    public async Task TestModbusPolling()
    {
        var logger = _loggerFactory.CreateLogger<ModbusConnectionTest>();
        logger.LogInformation("=== Testando polling Modbus ===");

        var config = new ModbusConnectionConfig
        {
            IsTcp = true,
            Host = "localhost",
            Port = 502,
            TimeoutMs = 5000,
            AutoPoll = true,
            PollIntervalMs = 1000
        };

        using var driver = new ModbusDriver(config, _loggerFactory.CreateLogger<ModbusDriver>());

        driver.TagValueChanged += (sender, e) =>
        {
            logger.LogInformation("Tag alterada: {Address} = {Value} às {Timestamp}", 
                e.Address, e.Value, e.Timestamp);
        };

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado, registrando tags para polling...");

            // Registrar tags para polling automático
            driver.RegisterAddressForPolling("1:100:03", DataType.Int16);
            driver.RegisterAddressForPolling("1:101:03", DataType.Int16);
            driver.RegisterAddressForPolling("1:200:03", DataType.Float);

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
    /// Teste com simulador Modbus (usando pymodbus ou similar)
    /// </summary>
    public async Task TestModbusSimulator()
    {
        var logger = _loggerFactory.CreateLogger<ModbusConnectionTest>();
        logger.LogInformation("=== Testando com simulador Modbus ===");

        // Supondo que você tenha um simulador rodando em localhost:5020
        var config = new ModbusConnectionConfig
        {
            IsTcp = true,
            Host = "localhost",
            Port = 5020,
            TimeoutMs = 5000,
            AutoPoll = false
        };

        using var driver = new ModbusDriver(config, _loggerFactory.CreateLogger<ModbusDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado ao simulador!");

            // Testar diferentes tipos de dados
            var intTag = await driver.ReadTagAsync("1:0:03", DataType.Int16);
            logger.LogInformation("Int16: {Value}", intTag.Value);

            var floatTag = await driver.ReadTagAsync("1:2:03", DataType.Float);
            logger.LogInformation("Float: {Value}", floatTag.Value);

            var boolTag = await driver.ReadTagAsync("1:0:01", DataType.Bool);
            logger.LogInformation("Bool: {Value}", boolTag.Value);

            await driver.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste com simulador");
            throw;
        }
    }
}
