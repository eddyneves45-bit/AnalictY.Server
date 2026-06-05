using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal class GatewayAppService : IGatewayAppService
{
    private readonly IGatewayService _gatewayService;

    public GatewayAppService(IGatewayService gatewayService)
    {
        _gatewayService = gatewayService;
    }

    public object GetGatewayHealth()
    {
        return _gatewayService.GetAllModuleStates();
    }

    public async Task<object> GetModuleHealthAsync(string moduleName)
    {
        var health = await _gatewayService.GetModuleHealthAsync(moduleName);
        return new { moduleName, health };
    }

    public async Task<object> RouteRequestAsync(GatewayRequest request)
    {
        return await _gatewayService.RouteRequestAsync(request);
    }
}
