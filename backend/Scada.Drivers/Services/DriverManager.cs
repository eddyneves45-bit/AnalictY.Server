using Scada.Drivers.Interfaces;
using Scada.Drivers.Models;

namespace Scada.Drivers.Services;

public class DriverManager
{
    private readonly Dictionary<string, IDriver> _drivers = new();

    public void RegisterDriver(string key, IDriver driver)
    {
        _drivers[key] = driver;
    }

    public IDriver? GetDriver(string key)
    {
        return _drivers.GetValueOrDefault(key);
    }

    public IEnumerable<IDriver> GetAllDrivers()
    {
        return _drivers.Values;
    }

    public async Task<DriverState> GetDriverStateAsync(string key)
    {
        var driver = GetDriver(key);
        if (driver == null)
        {
            return new DriverState(key, "unknown", DriverStatus.Error, DateTime.UtcNow, "Driver not found");
        }

        var isHealthy = await driver.IsHealthyAsync();
        var status = driver.IsConnected ? DriverStatus.Connected : DriverStatus.Disconnected;

        return new DriverState(key, driver.Type, status, DateTime.UtcNow);
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var driver in _drivers.Values)
        {
            if (driver.IsConnected)
            {
                await driver.DisconnectAsync();
            }
        }
    }
}
