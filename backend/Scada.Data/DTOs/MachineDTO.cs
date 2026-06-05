namespace Scada.Data.DTOs;

public record MachineDTO(
    int Id,
    string Name,
    string Code,
    string CostCenter,
    string Location,
    bool IsActive
);

public record CreateMachineDTO(
    string Name,
    string Code,
    string CostCenter,
    string Location,
    bool IsActive = true
);

public record UpdateMachineDTO(
    int Id,
    string? Name = null,
    string? Code = null,
    string? CostCenter = null,
    string? Location = null,
    bool? IsActive = null
);
