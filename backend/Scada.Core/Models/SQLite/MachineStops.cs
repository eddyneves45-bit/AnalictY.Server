namespace Scada.Core.Models.SQLite;

public class MachineStops
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public double DurationSeconds { get; set; }
}
