namespace Scada.Core.Models.SQLite;

public class Report
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ReportType { get; set; } = string.Empty; // production, downtime, oee, custom
    public string? Schedule { get; set; } // cron expression
    public string? Parameters { get; set; } // JSON
    public string? MachineId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
