using Scada.Monitoring.Interfaces;
using Scada.Monitoring.Models;

namespace Scada.Monitoring.Services;

public class MetricsCollector : IMetricsCollector
{
    private readonly Dictionary<int, MachineMetrics> _metricsHistory = new();
    private readonly Dictionary<int, MachineMetrics> _currentMetrics = new();

    public void UpdateMetrics(MachineMetrics metrics)
    {
        _metricsHistory[metrics.MachineId] = metrics;
        _currentMetrics[metrics.MachineId] = metrics;
    }

    public async Task<MachineMetrics> CollectMachineMetricsAsync(int machineId, DateTime from, DateTime to)
    {
        // TODO: Implementar coleta real de métricas do banco de dados
        await Task.Delay(10);
        
        if (_metricsHistory.TryGetValue(machineId, out var metrics))
        {
            return metrics;
        }

        return new MachineMetrics(machineId, 0, 0, 0, 0, DateTime.UtcNow);
    }

    public async Task<List<MachineMetrics>> CollectAllMachineMetricsAsync(DateTime from, DateTime to)
    {
        // TODO: Implementar coleta real de métricas de todas as máquinas
        await Task.Delay(10);
        return new List<MachineMetrics>(_metricsHistory.Values);
    }

    public async Task<MachineMetrics> GetCurrentMachineMetricsAsync(int machineId)
    {
        await Task.CompletedTask;
        
        if (_currentMetrics.TryGetValue(machineId, out var metrics))
        {
            return metrics;
        }

        return new MachineMetrics(machineId, 0, 0, 0, 0, DateTime.UtcNow);
    }
}
