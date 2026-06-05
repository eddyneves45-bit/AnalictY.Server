namespace Scada.Core.Models.SQLite;

public class EventQueue
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty; // STATE_TRANSITION, STOP_START, STOP_END
    public string MachineId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public string Status { get; set; } = "pending"; // pending, processing, completed, failed
}
