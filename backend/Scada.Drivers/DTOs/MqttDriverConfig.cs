namespace Scada.Drivers.DTOs;

public record MqttDriverConfig(
    string BrokerUrl,
    string ClientId,
    string? Username = null,
    string? Password = null,
    bool UseTls = false,
    int Port = 1883,
    string? CaCertificatePath = null,
    string? ClientCertificatePath = null,
    string? ClientKeyPath = null,
    int Qos = 0
);
