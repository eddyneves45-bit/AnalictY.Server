namespace Scada.Drivers.DTOs;

public record ModbusDriverConfig(
    string Host,
    int Port = 502,
    byte SlaveId = 1,
    int TimeoutMs = 5000
);
