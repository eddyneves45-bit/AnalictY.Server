using Scada.Gateway.Interfaces;

namespace Scada.Gateway.Models;

public record ModuleState(
    string ModuleName,
    ModuleHealthStatus Status,
    DateTime LastUpdated,
    int RequestCount,
    int ErrorCount,
    double AverageResponseTimeMs,
    string? ErrorMessage = null
);
