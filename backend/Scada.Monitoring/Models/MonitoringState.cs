using Scada.Monitoring.Interfaces;

namespace Scada.Monitoring.Models;

public record MonitoringState(
    Dictionary<int, MachineMetrics> MachineMetricsHistory,
    Dictionary<string, Alert> ActiveAlerts,
    DateTime LastUpdated
);
