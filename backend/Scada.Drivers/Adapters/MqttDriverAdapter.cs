using Scada.Drivers.DTOs;
using Scada.Drivers.Interfaces;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Scada.Drivers.Adapters;

public class MqttDriverAdapter : IMqttDriver
{
    private readonly MqttDriverConfig _config;
    private readonly IMqttClient _client;
    private readonly Dictionary<string, Action<string, string>> _subscriptions = new();

    public string Name => "MQTT Driver";
    public string Type => "mqtt";
    public bool IsConnected => _client.IsConnected;

    public MqttDriverAdapter(MqttDriverConfig config)
    {
        _config = config;
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            foreach (var subscription in _subscriptions)
            {
                if (TopicMatches(subscription.Key, e.ApplicationMessage.Topic))
                {
                    subscription.Value(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment));
                }
            }

            return Task.CompletedTask;
        };
    }

    public async Task ConnectAsync()
    {
        if (_client.IsConnected)
        {
            return;
        }

        var builder = new MqttClientOptionsBuilder()
            .WithClientId(string.IsNullOrWhiteSpace(_config.ClientId) ? $"scada-{Guid.NewGuid():N}" : _config.ClientId)
            .WithTcpServer(_config.BrokerUrl, _config.Port)
            .WithCleanSession(true)
            .WithProtocolVersion(MqttProtocolVersion.V500);

        if (!string.IsNullOrWhiteSpace(_config.Username))
        {
            builder.WithCredentials(_config.Username, _config.Password ?? string.Empty);
        }

        if (_config.UseTls)
        {
            builder.WithTlsOptions(BuildTlsOptions());
        }

        var result = await _client.ConnectAsync(builder.Build());
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"Falha ao conectar MQTT: {result.ResultCode} - {result.ReasonString}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _subscriptions.Clear();
    }

    public Task<bool> IsHealthyAsync()
    {
        return Task.FromResult(_client.IsConnected);
    }

    public async Task PublishAsync(string topic, string payload)
    {
        if (!_client.IsConnected)
            throw new InvalidOperationException("Driver not connected");

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(ToQualityOfServiceLevel(_config.Qos))
            .Build();

        await _client.PublishAsync(message);
    }

    public async Task SubscribeAsync(string topic, Action<string, string> callback)
    {
        if (!_client.IsConnected)
            throw new InvalidOperationException("Driver not connected");

        _subscriptions[topic] = callback;
        await _client.SubscribeAsync(topic, ToQualityOfServiceLevel(_config.Qos));
    }

    public async Task UnsubscribeAsync(string topic)
    {
        if (_client.IsConnected)
        {
            await _client.UnsubscribeAsync(topic);
        }

        _subscriptions.Remove(topic);
    }

    private MqttClientTlsOptions BuildTlsOptions()
    {
        var tlsBuilder = new MqttClientTlsOptionsBuilder()
            .UseTls()
            .WithSslProtocols(SslProtocols.Tls12);

        var clientCertificate = LoadClientCertificate();
        if (clientCertificate != null)
        {
            if (!clientCertificate.HasPrivateKey)
            {
                throw new InvalidOperationException("O certificado cliente MQTT não contém chave privada.");
            }

            tlsBuilder.WithClientCertificates(new List<X509Certificate2> { clientCertificate });
        }

        var caCertificate = LoadCertificate(_config.CaCertificatePath);
        if (caCertificate != null)
        {
            tlsBuilder.WithCertificateValidationHandler(context =>
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return context.Certificate is X509Certificate2 serverCertificate && chain.Build(serverCertificate);
            });
        }

        return tlsBuilder.Build();
    }

    private X509Certificate2? LoadClientCertificate()
    {
        var clientCertPath = ResolvePath(_config.ClientCertificatePath);
        if (clientCertPath == null)
        {
            return null;
        }

        var extension = Path.GetExtension(clientCertPath);
        if (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return new X509Certificate2(
                clientCertPath,
                _config.Password ?? string.Empty,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }

        var clientKeyPath = ResolvePath(_config.ClientKeyPath);
        if (clientKeyPath == null)
        {
            return LoadCertificate(clientCertPath);
        }

        var pemCertificate = X509Certificate2.CreateFromPemFile(clientCertPath, clientKeyPath);
        var pfxBytes = pemCertificate.Export(X509ContentType.Pfx, string.Empty);
        return new X509Certificate2(
            pfxBytes,
            string.Empty,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    private static X509Certificate2? LoadCertificate(string? path)
    {
        var resolvedPath = ResolvePath(path);
        return resolvedPath == null ? null : new X509Certificate2(resolvedPath);
    }

    private static string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidates = Path.IsPathRooted(path)
            ? new[] { path }
            : new[]
            {
                Path.GetFullPath(path),
                Path.Combine(@"C:\certs", Path.GetFileName(path)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "certs", Path.GetFileName(path))),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "certs", Path.GetFileName(path))),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "certs", Path.GetFileName(path)))
            };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Arquivo de certificado MQTT não encontrado: {path}");
    }

    private static MqttQualityOfServiceLevel ToQualityOfServiceLevel(int qos)
    {
        return qos switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };
    }

    private static bool TopicMatches(string filter, string topic)
    {
        var filterParts = (filter ?? "").Split('/', StringSplitOptions.None);
        var topicParts = (topic ?? "").Split('/', StringSplitOptions.None);

        for (var i = 0; i < filterParts.Length; i++)
        {
            var part = filterParts[i];
            if (part == "#")
            {
                return true;
            }

            if (i >= topicParts.Length)
            {
                return false;
            }

            if (part != "+" && !string.Equals(part, topicParts[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterParts.Length == topicParts.Length;
    }
}
