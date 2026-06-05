using Scada.Monitoring.Interfaces;

namespace Scada.Api.Services;

internal class MonitoringAppService : IMonitoringAppService
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly IAlertManager _alertManager;

    public MonitoringAppService(IMetricsCollector metricsCollector, IAlertManager alertManager)
    {
        _metricsCollector = metricsCollector;
        _alertManager = alertManager;
    }

    public async Task<object> GetMachineMetricsAsync(int machineId, DateTime from, DateTime to)
    {
        return await _metricsCollector.CollectMachineMetricsAsync(machineId, from, to);
    }

    public async Task<object> GetAllMachineMetricsAsync(DateTime from, DateTime to)
    {
        return await _metricsCollector.CollectAllMachineMetricsAsync(from, to);
    }

    public async Task<object> GetCurrentMachineMetricsAsync(int machineId)
    {
        return await _metricsCollector.GetCurrentMachineMetricsAsync(machineId);
    }

    public async Task<object> CreateAlertAsync(AlertRequest request)
    {
        return await _alertManager.CreateAlertAsync(request);
    }

    public async Task<object> GetActiveAlertsAsync()
    {
        return await _alertManager.GetActiveAlertsAsync();
    }

    public async Task<object> AcknowledgeAlertAsync(string alertId)
    {
        return await _alertManager.AcknowledgeAlertAsync(alertId);
    }

    public async Task<object> ResolveAlertAsync(string alertId)
    {
        var success = await _alertManager.ResolveAlertAsync(alertId);
        return new { success };
    }
}
