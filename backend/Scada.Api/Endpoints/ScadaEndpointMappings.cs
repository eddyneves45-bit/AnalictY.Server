using Scada.Api.Endpoints;

public static class ScadaEndpointMappings
{
    public static WebApplication MapScadaEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapAuditEndpoints();
        app.MapMachineEndpoints();
        app.MapConfigEndpoints();
        app.MapRuntimeEndpoints();
        app.MapOeeEndpoints();
        app.MapAlertEndpoints();
        app.MapAlertRuleEndpoints();
        app.MapNotificationEndpoints();
        app.MapDashboardEndpoints();
        app.MapBiEndpoints();
        app.MapSimulatorEndpoints();
        app.MapReportEndpoints();
        app.MapStateEndpoints();
        app.MapGatewayEndpoints();
        app.MapDriverEndpoints();
        app.MapMonitoringEndpoints();
        app.MapIndustrialHealthEndpoints();
        app.MapTagHistoryEndpoints();
        app.MapMetricsEndpoints();
        app.MapLogEndpoints();
        app.MapDowntimeEndpoints();
        app.MapProductionDiagnosticEndpoints();
        app.MapWeintekEndpoints();
        app.MapFtpExportEndpoints();
        app.MapDatabaseBrowserEndpoints();
        app.MapSystemEndpoints();
        app.MapAdminEndpoints();

        return app;
    }
}
