namespace Scada.Core.Models.SQLite;

public class CauseStats
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; } = 0;
    public double TotalDurationSeconds { get; set; } = 0;
    public double Weight { get; set; } = 1.0; // Weight adaptativo
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
