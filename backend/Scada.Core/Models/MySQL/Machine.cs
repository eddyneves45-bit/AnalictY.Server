namespace Scada.Core.Models.MySQL;

public class Machine
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CostCenter { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
