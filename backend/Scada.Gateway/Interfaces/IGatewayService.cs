namespace Scada.Gateway.Interfaces;

public interface IGatewayService
{
    Task<GatewayResponse> RouteRequestAsync(GatewayRequest request);
    Task<bool> IsModuleHealthyAsync(string moduleName);
    Task<ModuleHealthStatus> GetModuleHealthAsync(string moduleName);
    Dictionary<string, Scada.Gateway.Models.ModuleState> GetAllModuleStates();
}

public record GatewayRequest(string ModuleName, string Action, Dictionary<string, object> Parameters);
public record GatewayResponse(bool Success, object? Data = null, string? Error = null);
