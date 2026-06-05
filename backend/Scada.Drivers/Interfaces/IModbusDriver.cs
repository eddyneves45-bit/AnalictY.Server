namespace Scada.Drivers.Interfaces;

public interface IModbusDriver : IDriver
{
    Task<short?> ReadHoldingRegisterAsync(byte slaveId, ushort address);
    Task<bool> WriteHoldingRegisterAsync(byte slaveId, ushort address, short value);
    Task<short[]> ReadHoldingRegistersAsync(byte slaveId, ushort address, ushort count);
}
