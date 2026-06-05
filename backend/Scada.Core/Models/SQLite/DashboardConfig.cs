namespace Scada.Core.Models.SQLite;

public class DashboardConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string PeriodPreset { get; set; } = "today";
    public string RefreshInterval { get; set; } = "10";
    public string WidgetsJson { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
