namespace Scada.Core.Models.SQLite;

public class MqttRuntime
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public string State { get; set; } = "DISCONNECTED"; // CONNECTED, DISCONNECTED, ERROR, CONNECTING
    public string LastStatus { get; set; } = "UNKNOWN"; // "OK", "Timeout", "Auth failed"
    public string? LastError { get; set; } // erro completo (debug)
    public DateTime? LastSeen { get; set; } // último heartbeat
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
