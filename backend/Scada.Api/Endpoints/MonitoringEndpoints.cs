using Scada.Api.Services;
using Scada.Monitoring.Interfaces;

public static class MonitoringEndpoints
{
    public static WebApplication MapMonitoringEndpoints(this WebApplication app)
    {
        app.MapGet("/api/monitoring/metrics/{machineId}", async (
            int machineId,
            DateTime from,
            DateTime to,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.GetMachineMetricsAsync(machineId, from, to));
        })
        .WithName("GetMachineMetrics");

        app.MapGet("/api/monitoring/metrics", async (
            DateTime from,
            DateTime to,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.GetAllMachineMetricsAsync(from, to));
        })
        .WithName("GetAllMachineMetrics");

        app.MapGet("/api/monitoring/metrics/current/{machineId}", async (
            int machineId,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.GetCurrentMachineMetricsAsync(machineId));
        })
        .WithName("GetCurrentMachineMetrics");

        app.MapPost("/api/monitoring/alerts", async (
            AlertRequest request,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.CreateAlertAsync(request));
        })
        .WithName("CreateMonitoringAlert");

        app.MapGet("/api/monitoring/alerts/active", async (IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.GetActiveAlertsAsync());
        })
        .WithName("GetActiveAlerts");

        app.MapPost("/api/monitoring/alerts/{alertId}/acknowledge", async (
            string alertId,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.AcknowledgeAlertAsync(alertId));
        })
        .WithName("AcknowledgeMonitoringAlert");

        app.MapPost("/api/monitoring/alerts/{alertId}/resolve", async (
            string alertId,
            IMonitoringAppService monitoringService) =>
        {
            return Results.Ok(await monitoringService.ResolveAlertAsync(alertId));
        })
        .WithName("ResolveAlert");

        return app;
    }
}
