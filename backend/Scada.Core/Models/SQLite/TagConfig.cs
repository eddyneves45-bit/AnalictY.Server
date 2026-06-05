namespace Scada.Core.Models.SQLite;

public class TagConfig
{
    public int Id { get; set; }
    public int? FolderId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty; // Bool, Int16, Int32, Float, Double, String
    public string DriverType { get; set; } = string.Empty; // OPCUA, MQTT, Modbus, EthernetIP
    public string PersistenceMode { get; set; } = "mes"; // mes, telemetry
    public string Address { get; set; } = string.Empty;
    public int? OpcuaConnectionId { get; set; }
    public int? MqttConnectionId { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
