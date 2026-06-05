namespace Scada.Monitoring.Interfaces;

public interface IAlertManager
{
    Task<Alert> CreateAlertAsync(AlertRequest request);
    Task<Alert?> GetAlertAsync(string alertId);
    Task<List<Alert>> GetActiveAlertsAsync();
    Task<bool> AcknowledgeAlertAsync(string alertId);
    Task<bool> ResolveAlertAsync(string alertId);
}

public record AlertRequest(
    string MachineId,
    AlertType Type,
    string Message,
    AlertSeverity Severity
);

public record Alert(
    string Id,
    string MachineId,
    AlertType Type,
    string Message,
    AlertSeverity Severity,
    DateTime CreatedAt,
    AlertStatus Status,
    DateTime? AcknowledgedAt = null,
    DateTime? ResolvedAt = null
);

public enum AlertType
{
    Production,
    Quality,
    Downtime,
    Maintenance,
    System
}

public enum AlertSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved
}
