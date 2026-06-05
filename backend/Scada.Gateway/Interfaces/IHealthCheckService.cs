namespace Scada.Gateway.Interfaces;

public interface IHealthCheckService
{
    Task<bool> CheckModuleHealthAsync(string moduleName);
    Task<Dictionary<string, ModuleHealthStatus>> CheckAllModulesHealthAsync();
}

public enum ModuleHealthStatus
{
    Healthy,
    Unhealthy,
    Degraded,
    Unknown
}
