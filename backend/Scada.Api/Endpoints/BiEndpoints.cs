using Scada.Api.Services;

public static class BiEndpoints
{
    public static WebApplication MapBiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/bi/indicators", async (
            string? cost_center,
            string? machine_id,
            DateTime? from_date,
            DateTime? to_date,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetIndicatorsAsync(cost_center, machine_id, from_date, to_date, cancellationToken));
        })
        .WithName("GetBIIndicators");

        app.MapGet("/api/bi/cost-centers", async (
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetCostCentersAsync(cancellationToken));
        })
        .WithName("GetCostCenters");

        app.MapGet("/api/bi/machines", async (
            string cost_center,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetMachinesAsync(cost_center, cancellationToken));
        })
        .WithName("GetBIMachines");

        app.MapGet("/api/bi/machines/{machineId}/overview", async (
            string machineId,
            DateTime from,
            DateTime to,
            string? target_mode,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetMachineOverviewAsync(machineId, from, to, target_mode, cancellationToken));
        })
        .WithName("GetBIMachineOverview");

        app.MapGet("/api/bi/machines/summaries", async (
            DateTime from,
            DateTime to,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetMachineSummariesAsync(from, to, cancellationToken));
        })
        .WithName("GetBIMachineSummaries");

        app.MapGet("/api/bi/machines/{machineId}/production-by-shift", async (
            string machineId,
            DateOnly date,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await biService.GetMachineProductionByShiftAsync(machineId, date, cancellationToken));
        })
        .WithName("GetBIMachineProductionByShift");

        app.MapGet("/api/export/production/csv", async (
            string? machine_id,
            DateTime? from_date,
            DateTime? to_date,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            var csv = await biService.ExportProductionCsvAsync(machine_id, from_date, to_date, cancellationToken);
            return Results.Text(csv, "text/csv");
        })
        .WithName("ExportProductionCSV")
        .RequireAuthorization("CanDownloadReports");

        app.MapGet("/api/export/downtime/csv", async (
            string? machine_id,
            DateTime? from_date,
            DateTime? to_date,
            IBiService biService,
            CancellationToken cancellationToken) =>
        {
            var csv = await biService.ExportDowntimeCsvAsync(machine_id, from_date, to_date, cancellationToken);
            return Results.Text(csv, "text/csv");
        })
        .WithName("ExportDowntimeCSV")
        .RequireAuthorization("CanDownloadReports");

        return app;
    }
}
