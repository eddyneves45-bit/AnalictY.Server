namespace Scada.Core.Models.MySQL;

public class MachineSnapshot
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public DateTime SnapshotTime { get; set; } = DateTime.UtcNow;
    public double AccumulatedTimeRunning { get; set; }
    public double AccumulatedTimeStopped { get; set; }
    public double AccumulatedTimeIdle { get; set; }
    public int EventCount { get; set; }
}
