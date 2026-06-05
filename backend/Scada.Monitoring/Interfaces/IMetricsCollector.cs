namespace Scada.Monitoring.Interfaces;

public interface IMetricsCollector
{
    Task<MachineMetrics> CollectMachineMetricsAsync(int machineId, DateTime from, DateTime to);
    Task<List<MachineMetrics>> CollectAllMachineMetricsAsync(DateTime from, DateTime to);
    Task<MachineMetrics> GetCurrentMachineMetricsAsync(int machineId);
}

public record MachineMetrics(
    int MachineId,
    double ProductionCount,
    double GoodCount,
    double BadCount,
    double DowntimeMinutes,
    DateTime Timestamp
);
