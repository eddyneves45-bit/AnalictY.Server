namespace Scada.Drivers.Models;

public enum DriverStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public record DriverState(
    string DriverId,
    string DriverType,
    DriverStatus Status,
    DateTime LastUpdated,
    string? ErrorMessage = null
);
