using Scada.Gateway.Interfaces;

namespace Scada.Gateway.DTOs;

public record ModuleHealthDTO(
    string ModuleName,
    ModuleHealthStatus Status,
    DateTime LastChecked,
    string? Message = null
);
