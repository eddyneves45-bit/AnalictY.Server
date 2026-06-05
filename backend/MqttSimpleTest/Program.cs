using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace MqttSimpleTest;

class Program
{
    static async Task Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<Program>();
        
        logger.LogInformation("=== Teste MQTT AWS IoT ===");
        logger.LogInformation("Endpoint: a2j2mrlwb08rz9-ats.iot.sa-east-1.amazonaws.com");
        logger.LogInformation("Client ID: iotconsole-f7cc8a61-f2b5-4878-9d0a-46526f9151a8");
        logger.LogInformation("Tópico: scada/test");

        var mqttFactory = new MqttFactory();
        var mqttClient = mqttFactory.CreateMqttClient();

        var certsPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "certs");
        
        logger.LogInformation("Carregando certificados de: {CertsPath}", certsPath);

        var clientCertPath = Path.Combine(certsPath, "7b738dad0bdb84d7e6bce9160ec163d4fa5cf34e43f236feb96013f35b4562d2-certificate.pem.crt");
        var clientKeyPath = Path.Combine(certsPath, "7b738dad0bdb84d7e6bce9160ec163d4fa5cf34e43f236feb96013f35b4562d2-private.pem.key");
        var caCertPath = Path.Combine(certsPath, "AmazonRootCA1.pem");

        try
        {
            logger.LogInformation("Criando certificado PFX a partir de PEM+KEY com .NET 8...");

            var pemCertificate = X509Certificate2.CreateFromPemFile(clientCertPath, clientKeyPath);
            var pfxBytes = pemCertificate.Export(X509ContentType.Pfx, "");
            var pfxPath = Path.Combine(certsPath, "aws-device.pfx");
            File.WriteAllBytes(pfxPath, pfxBytes);

            var certificate = new X509Certificate2(
                pfxPath,
                "",
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable
            );
            
            logger.LogInformation("PFX criado: {Subject}", certificate.Subject);
            logger.LogInformation("HasPrivateKey: {HasPrivateKey}", certificate.HasPrivateKey);
            
            // Configuração TLS correta para AWS IoT
            var tlsOptions = new MqttClientTlsOptionsBuilder()
                .UseTls()
                .WithClientCertificates(new List<System.Security.Cryptography.X509Certificates.X509Certificate2> { certificate })
                .WithSslProtocols(SslProtocols.Tls12)
                .WithCertificateValidationHandler((context) => true) // AWS IoT usa certificado cliente
                .Build();

            var options = new MqttClientOptionsBuilder()
                .WithClientId("iotconsole-f7cc8a61-f2b5-4878-9d0a-46526f9151a8")
                .WithTcpServer("a2j2mrlwb08rz9-ats.iot.sa-east-1.amazonaws.com", 8883)
                .WithTlsOptions(tlsOptions)
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithCleanSession(true)
                .Build();

            logger.LogInformation("Conectando ao broker MQTT...");
            var result = await mqttClient.ConnectAsync(options);

            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                logger.LogInformation("Conectado com sucesso!");
                
                // Subscrever ao tópico
                logger.LogInformation("Subscrevendo ao tópico: scada/test");
                await mqttClient.SubscribeAsync("scada/test");

                // Publicar mensagem de teste
                logger.LogInformation("Publicando mensagem de teste...");
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("scada/test")
                    .WithPayload($"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"value\":123.45,\"device\":\"test\"}}")
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await mqttClient.PublishAsync(message);
                logger.LogInformation("Mensagem publicada");

                // Aguardar mensagens
                mqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    var payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray());
                    logger.LogInformation("Mensagem recebida no tópico {Topic}: {Payload}", 
                        e.ApplicationMessage.Topic, payload);
                    return Task.CompletedTask;
                };

                logger.LogInformation("Aguardando mensagens por 10 segundos...");
                await Task.Delay(10000);

                logger.LogInformation("Desconectando...");
                await mqttClient.DisconnectAsync();
                logger.LogInformation("Teste concluído com sucesso!");
            }
            else
            {
                logger.LogError("Falha na conexão: {ResultCode} - {Reason}", 
                    result.ResultCode, result.ReasonString);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste MQTT");
            logger.LogError("Mensagem: {Message}", ex.Message);
            logger.LogError("StackTrace: {StackTrace}", ex.StackTrace);
        }
        
        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
