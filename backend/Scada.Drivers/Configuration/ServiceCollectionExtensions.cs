using Microsoft.Extensions.DependencyInjection;
using Scada.Drivers.Services;

namespace Scada.Drivers.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDriversModule(this IServiceCollection services)
    {
        services.AddSingleton<DriverManager>();
        return services;
    }
}
