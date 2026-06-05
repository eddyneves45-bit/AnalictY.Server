namespace Scada.Core.Models.SQLite;

public class MachineTagMap
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int TagConfigId { get; set; }
    public string TagAlias { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
