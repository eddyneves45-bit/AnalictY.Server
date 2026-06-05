namespace Scada.Core.Models.SQLite;

public class TagRuntimeSnapshot
{
    public int TagId { get; set; }
    public string ValueJson { get; set; } = "null";
    public string Quality { get; set; } = "UNKNOWN";
    public DateTime SourceTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime LastPersistedAt { get; set; } = DateTime.UtcNow;
}
