namespace Scada.Core.Models;

public class Tag
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DriverType { get; set; } = string.Empty; // MQTT, OPCUA, MODBUS, ETHERNETIP
    public string Address { get; set; } = string.Empty; // Endereço no dispositivo
    public DataType DataType { get; set; }
    public object? Value { get; set; }
    public DateTime? LastUpdate { get; set; }
    public TagQuality Quality { get; set; } = TagQuality.Good;
    public bool EnableLogging { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? AlarmHigh { get; set; }
    public double? AlarmLow { get; set; }
}

public enum DataType
{
    Bool,
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float,
    Double,
    String
}

public enum TagQuality
{
    Good,
    Bad,
    Uncertain,
    Disconnected
}
