using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal interface IGatewayAppService
{
    object GetGatewayHealth();
    Task<object> GetModuleHealthAsync(string moduleName);
    Task<object> RouteRequestAsync(GatewayRequest request);
}
