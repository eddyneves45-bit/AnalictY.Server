using Scada.Gateway.Interfaces;

namespace Scada.Gateway.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly Dictionary<string, ModuleHealthStatus> _moduleHealthStatus = new();

    public void SetModuleHealth(string moduleName, ModuleHealthStatus status)
    {
        _moduleHealthStatus[moduleName] = status;
    }

    public Task<bool> CheckModuleHealthAsync(string moduleName)
    {
        if (!_moduleHealthStatus.ContainsKey(moduleName))
            return Task.FromResult(false);

        return Task.FromResult(_moduleHealthStatus[moduleName] == ModuleHealthStatus.Healthy);
    }

    public Task<Dictionary<string, ModuleHealthStatus>> CheckAllModulesHealthAsync()
    {
        return Task.FromResult(new Dictionary<string, ModuleHealthStatus>(_moduleHealthStatus));
    }
}
