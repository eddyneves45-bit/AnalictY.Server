namespace Scada.Data.Models;

public record MachineEntity(
    int Id,
    string Name,
    string Code,
    string CostCenter,
    string Location,
    bool IsActive,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default
);
