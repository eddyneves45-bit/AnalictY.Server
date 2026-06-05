using Microsoft.Extensions.DependencyInjection;
using Scada.Monitoring.Interfaces;
using Scada.Monitoring.Services;

namespace Scada.Monitoring.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        services.AddSingleton<IMetricsCollector, MetricsCollector>();
        services.AddSingleton<IAlertManager, AlertManager>();
        return services;
    }
}
