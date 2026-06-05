namespace Scada.Monitoring.DTOs;

public record MetricsQueryDTO(
    int MachineId,
    DateTime From,
    DateTime To
);

public record MetricsResponseDTO(
    int MachineId,
    double ProductionCount,
    double GoodCount,
    double BadCount,
    double DowntimeMinutes,
    double OEE,
    DateTime Timestamp
);
