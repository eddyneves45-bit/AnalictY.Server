using Scada.Monitoring.Interfaces;

namespace Scada.Api.Services;

internal interface IMonitoringAppService
{
    Task<object> GetMachineMetricsAsync(int machineId, DateTime from, DateTime to);
    Task<object> GetAllMachineMetricsAsync(DateTime from, DateTime to);
    Task<object> GetCurrentMachineMetricsAsync(int machineId);
    Task<object> CreateAlertAsync(AlertRequest request);
    Task<object> GetActiveAlertsAsync();
    Task<object> AcknowledgeAlertAsync(string alertId);
    Task<object> ResolveAlertAsync(string alertId);
}
