namespace Scada.Core.Models.SQLite;

public class Alert
{
    public int Id { get; set; }
    public string AlertType { get; set; } = string.Empty; // downtime, target_not_met, quality_issue, custom
    public string Severity { get; set; } = string.Empty; // low, medium, high, critical
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? MachineId { get; set; }
    public string? Metadata { get; set; } // JSON
    public bool IsAcknowledged { get; set; } = false;
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
