using Scada.Gateway.Interfaces;
using Scada.Gateway.Models;

namespace Scada.Gateway.Services;

public class GatewayService : IGatewayService
{
    private readonly Dictionary<string, ModuleState> _moduleStates = new();
    private readonly Dictionary<string, Func<GatewayRequest, Task<GatewayResponse>>> _moduleHandlers = new();

    public void RegisterModule(string moduleName, Func<GatewayRequest, Task<GatewayResponse>> handler)
    {
        _moduleHandlers[moduleName] = handler;
        _moduleStates[moduleName] = new ModuleState(
            ModuleName: moduleName,
            Status: ModuleHealthStatus.Unknown,
            LastUpdated: DateTime.UtcNow,
            RequestCount: 0,
            ErrorCount: 0,
            AverageResponseTimeMs: 0
        );
    }

    public async Task<GatewayResponse> RouteRequestAsync(GatewayRequest request)
    {
        if (!_moduleHandlers.ContainsKey(request.ModuleName))
        {
            return new GatewayResponse(false, Error: $"Module '{request.ModuleName}' not registered");
        }

        var startTime = DateTime.UtcNow;
        var state = _moduleStates[request.ModuleName];
        
        try
        {
            var handler = _moduleHandlers[request.ModuleName];
            var response = await handler(request);
            
            // Atualizar estado do módulo
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _moduleStates[request.ModuleName] = state with
            {
                RequestCount = state.RequestCount + 1,
                AverageResponseTimeMs = (state.AverageResponseTimeMs * state.RequestCount + responseTime) / (state.RequestCount + 1),
                LastUpdated = DateTime.UtcNow,
                Status = ModuleHealthStatus.Healthy
            };

            return response;
        }
        catch (Exception ex)
        {
            // Atualizar estado do módulo com erro
            _moduleStates[request.ModuleName] = state with
            {
                RequestCount = state.RequestCount + 1,
                ErrorCount = state.ErrorCount + 1,
                LastUpdated = DateTime.UtcNow,
                Status = ModuleHealthStatus.Unhealthy,
                ErrorMessage = ex.Message
            };

            return new GatewayResponse(false, Error: ex.Message);
        }
    }

    public Task<bool> IsModuleHealthyAsync(string moduleName)
    {
        if (!_moduleStates.ContainsKey(moduleName))
            return Task.FromResult(false);

        var state = _moduleStates[moduleName];
        return Task.FromResult(state.Status == ModuleHealthStatus.Healthy);
    }

    public Task<ModuleHealthStatus> GetModuleHealthAsync(string moduleName)
    {
        if (!_moduleStates.ContainsKey(moduleName))
            return Task.FromResult(ModuleHealthStatus.Unknown);

        return Task.FromResult(_moduleStates[moduleName].Status);
    }

    public Dictionary<string, ModuleState> GetAllModuleStates()
    {
        return new Dictionary<string, ModuleState>(_moduleStates);
    }
}
