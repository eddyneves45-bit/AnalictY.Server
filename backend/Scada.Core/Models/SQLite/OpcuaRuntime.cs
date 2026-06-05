namespace Scada.Core.Models.SQLite;

public class OpcuaRuntime
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public string State { get; set; } = "DISCONNECTED"; // CONNECTED, DISCONNECTED, ERROR
    public string LastStatus { get; set; } = "UNKNOWN"; // "OK", "Timeout", "Auth failed"
    public string? LastError { get; set; } // erro completo (debug)
    public DateTime? LastSeen { get; set; } // último heartbeat
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
