using Microsoft.Extensions.Logging;
using Scada.Drivers.Mqtt;
using Scada.Core.Models;
using System.Diagnostics;

namespace Scada.Tests;

public class MqttConnectionTest
{
    private readonly ILoggerFactory _loggerFactory;

    public MqttConnectionTest()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Teste de conexão MQTT simples (sem TLS)
    /// </summary>
    public async Task TestMqttBasicConnection()
    {
        var logger = _loggerFactory.CreateLogger<MqttConnectionTest>();
        logger.LogInformation("=== Iniciando teste MQTT básico ===");

        var config = new MqttConnectionConfig
        {
            Host = "localhost", // Altere para seu broker MQTT
            Port = 1883,
            ClientId = "Scada_Test_Client",
            Username = "", // Opcional
            Password = "", // Opcional
            UseTls = false,
            AutoReconnect = true
        };

        using var driver = new MqttDriver(config, _loggerFactory.CreateLogger<MqttDriver>());

        try
        {
            // Conectar
            logger.LogInformation("Conectando ao broker MQTT...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com sucesso!");

            // Aguardar um pouco
            await Task.Delay(2000);

            // Escrever um valor
            logger.LogInformation("Publicando mensagem de teste...");
            await driver.WriteTagAsync("scada/test/value", 42.5, DataType.Double);

            // Ler o valor
            logger.LogInformation("Lendo valor...");
            var tag = await driver.ReadTagAsync("scada/test/value", DataType.Double);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            // Desconectar
            logger.LogInformation("Desconectando...");
            await driver.DisconnectAsync();
            logger.LogInformation("Teste concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste MQTT");
            throw;
        }
    }

    /// <summary>
    /// Teste de conexão MQTT com TLS
    /// </summary>
    public async Task TestMqttTlsConnection()
    {
        var logger = _loggerFactory.CreateLogger<MqttConnectionTest>();
        logger.LogInformation("=== Iniciando teste MQTT com TLS ===");

        var config = new MqttConnectionConfig
        {
            Host = "localhost", // Altere para seu broker MQTT
            Port = 8883, // Porta TLS padrão
            ClientId = "Scada_TLS_Test_Client",
            Username = "", // Opcional
            Password = "", // Opcional
            UseTls = true,
            
            // Configurações de certificado (ajuste conforme necessário)
            ClientCertificatePath = @"C:\certs\client.pfx", // Caminho do certificado cliente
            ClientCertificatePassword = "", // Senha do certificado
            CaCertificatePath = @"C:\certs\ca.crt", // Caminho do certificado CA
            
            // Em desenvolvimento, pode ser necessário permitir certificados não confiáveis
            AllowUntrustedCertificates = true,
            IgnoreCertificateChainErrors = true,
            IgnoreCertificateRevocationErrors = true,
            
            TlsProtocol = System.Security.Authentication.SslProtocols.Tls12,
            AutoReconnect = true
        };

        using var driver = new MqttDriver(config, _loggerFactory.CreateLogger<MqttDriver>());

        try
        {
            logger.LogInformation("Conectando ao broker MQTT com TLS...");
            await driver.ConnectAsync();
            logger.LogInformation("Conectado com TLS com sucesso!");

            // Testar leitura/escrita
            await driver.WriteTagAsync("scada/test/tls", "TLS Connected", DataType.String);
            var tag = await driver.ReadTagAsync("scada/test/tls", DataType.String);
            logger.LogInformation("Valor lido: {Value}", tag.Value);

            await driver.DisconnectAsync();
            logger.LogInformation("Teste TLS concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste MQTT TLS");
            throw;
        }
    }

    /// <summary>
    /// Teste de conexão com HiveMQ Cloud (exemplo de broker público com TLS)
    /// </summary>
    public async Task TestHiveMqCloudConnection()
    {
        var logger = _loggerFactory.CreateLogger<MqttConnectionTest>();
        logger.LogInformation("=== Testando conexão HiveMQ Cloud ===");

        var config = new MqttConnectionConfig
        {
            Host = "your-cluster.hivemq.cloud", // Substitua pelo seu cluster
            Port = 8883,
            ClientId = "Scada_HiveMQ_Test",
            Username = "your-username", // Substitua
            Password = "your-password", // Substitua
            UseTls = true,
            AllowUntrustedCertificates = false,
            AutoReconnect = true
        };

        using var driver = new MqttDriver(config, _loggerFactory.CreateLogger<MqttDriver>());

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado ao HiveMQ Cloud!");

            await driver.WriteTagAsync("scada/hivemq/test", DateTime.UtcNow.ToString(), DataType.String);
            
            await Task.Delay(2000);
            await driver.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro na conexão HiveMQ");
            throw;
        }
    }

    /// <summary>
    /// Teste de subscrição contínua
    /// </summary>
    public async Task TestMqttSubscription()
    {
        var logger = _loggerFactory.CreateLogger<MqttConnectionTest>();
        logger.LogInformation("=== Testando subscrição MQTT ===");

        var config = new MqttConnectionConfig
        {
            Host = "localhost",
            Port = 1883,
            ClientId = "Scada_Sub_Test",
            UseTls = false
        };

        using var driver = new MqttDriver(config, _loggerFactory.CreateLogger<MqttDriver>());

        driver.TagValueChanged += (sender, e) =>
        {
            logger.LogInformation("Tag alterada: {Address} = {Value} às {Timestamp}", 
                e.Address, e.Value, e.Timestamp);
        };

        try
        {
            await driver.ConnectAsync();
            logger.LogInformation("Conectado, aguardando mensagens...");

            // Subscrever lendo o tag
            await driver.ReadTagAsync("scada/#", DataType.String);
            
            // Aguardar mensagens por 30 segundos
            await Task.Delay(30000);
            
            await driver.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste de subscrição");
            throw;
        }
    }
}

/// <summary>
/// Programa principal para executar os testes
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var test = new MqttConnectionTest();
        
        Console.WriteLine("Selecione o teste:");
        Console.WriteLine("1 - Conexão básica MQTT (sem TLS)");
        Console.WriteLine("2 - Conexão MQTT com TLS");
        Console.WriteLine("3 - Conexão HiveMQ Cloud");
        Console.WriteLine("4 - Teste de subscrição");
        Console.Write("Opção: ");
        
        var option = Console.ReadLine();
        
        try
        {
            switch (option)
            {
                case "1":
                    await test.TestMqttBasicConnection();
                    break;
                case "2":
                    await test.TestMqttTlsConnection();
                    break;
                case "3":
                    await test.TestHiveMqCloudConnection();
                    break;
                case "4":
                    await test.TestMqttSubscription();
                    break;
                default:
                    Console.WriteLine("Opção inválida");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
        
        Console.WriteLine("Pressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
