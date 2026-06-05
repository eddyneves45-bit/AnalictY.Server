namespace Scada.Core.Models.SQLite;

public class MachineDowntimeReason
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
