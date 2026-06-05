namespace Scada.Core.Models.SQLite;

public class ConfigHistory
{
    public int Id { get; set; }
    public int ConfigId { get; set; }
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
