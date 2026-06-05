using Microsoft.Extensions.DependencyInjection;
using Scada.Gateway.Interfaces;
using Scada.Gateway.Services;

namespace Scada.Gateway.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayModule(this IServiceCollection services)
    {
        services.AddSingleton<IGatewayService, GatewayService>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();
        services.AddSingleton<ITagRuntimeService, TagRuntimeService>();
        return services;
    }
}
