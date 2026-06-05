namespace Scada.Core.Models.MySQL;

public class EventStore
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty; // UUID global único - fonte principal de idempotência
    public string MachineId { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public string FromContext { get; set; } = string.Empty;
    public string ToContext { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime EndTime { get; set; } = DateTime.UtcNow;
    public double Duration { get; set; }
    public int StatusWord { get; set; }
    public string Source { get; set; } = string.Empty; // OPCUA, MQTT
    public string Quality { get; set; } = "GOOD"; // GOOD, BAD, STALE
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
