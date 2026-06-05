namespace Scada.Core.Models.SQLite;

public class PendingMySqlEnvelope
{
    public long Id { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
