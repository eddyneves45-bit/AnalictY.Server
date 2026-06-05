using Scada.Monitoring.Interfaces;

namespace Scada.Monitoring.Services;

public class AlertManager : IAlertManager
{
    private readonly Dictionary<string, Alert> _alerts = new();

    public async Task<Alert> CreateAlertAsync(AlertRequest request)
    {
        var alertId = Guid.NewGuid().ToString();
        var alert = new Alert(
            Id: alertId,
            MachineId: request.MachineId,
            Type: request.Type,
            Message: request.Message,
            Severity: request.Severity,
            CreatedAt: DateTime.UtcNow,
            Status: AlertStatus.Active
        );
        
        _alerts[alertId] = alert;
        await Task.CompletedTask;
        return alert;
    }

    public async Task<Alert?> GetAlertAsync(string alertId)
    {
        await Task.CompletedTask;
        return _alerts.GetValueOrDefault(alertId);
    }

    public async Task<List<Alert>> GetActiveAlertsAsync()
    {
        await Task.CompletedTask;
        return _alerts.Values.Where(a => a.Status == AlertStatus.Active).ToList();
    }

    public async Task<bool> AcknowledgeAlertAsync(string alertId)
    {
        if (!_alerts.ContainsKey(alertId))
            return false;

        var alert = _alerts[alertId];
        _alerts[alertId] = alert with
        {
            Status = AlertStatus.Acknowledged,
            AcknowledgedAt = DateTime.UtcNow
        };
        
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> ResolveAlertAsync(string alertId)
    {
        if (!_alerts.ContainsKey(alertId))
            return false;

        var alert = _alerts[alertId];
        _alerts[alertId] = alert with
        {
            Status = AlertStatus.Resolved,
            ResolvedAt = DateTime.UtcNow
        };
        
        await Task.CompletedTask;
        return true;
    }
}
