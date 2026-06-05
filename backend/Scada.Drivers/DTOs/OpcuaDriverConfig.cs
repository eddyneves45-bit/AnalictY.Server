namespace Scada.Drivers.DTOs;

public record OpcuaDriverConfig(
    string EndpointUrl,
    string? Username = null,
    string? Password = null,
    bool UseSecurity = false,
    int TimeoutMs = 5000
);
