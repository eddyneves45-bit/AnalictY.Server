namespace Scada.Core.Models.MySQL;

public class Production
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int ShiftId { get; set; }
    public int ProductionCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
