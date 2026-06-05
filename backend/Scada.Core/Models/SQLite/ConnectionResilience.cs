namespace Scada.Core.Models.SQLite;

public class ConnectionResilience
{
    public int Id { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty; // MQTT, OPCUA
    public DateTime OfflineStartTime { get; set; }
    public DateTime? OfflineEndTime { get; set; }
    public double OfflineDurationSeconds { get; set; }
    public bool IsRecovered { get; set; } = false;
}
