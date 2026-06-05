namespace Scada.Data.Models;

public class TagRuntimeState
{
    public int TagId { get; set; }
    public object? Value { get; set; }
    public string Quality { get; set; } = "UNKNOWN";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Connected { get; set; } = false;
    public string? TagName { get; set; }
}

public enum TagQuality
{
    UNKNOWN,
    GOOD,
    BAD,
    STALE,
    DISCONNECTED
}
