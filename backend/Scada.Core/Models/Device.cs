namespace Scada.Core.Models;

public class Device
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DriverType DriverType { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime? LastConnection { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public List<Tag> Tags { get; set; } = new();
}

public enum DriverType
{
    Mqtt,
    OpcUa,
    ModbusTcp,
    ModbusRtu,
    EthernetIp
}
