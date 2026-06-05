using Scada.Api.Services;

public static class ReportEndpoints
{
    public static WebApplication MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports", async (
            string? machine_id,
            string? report_type,
            bool? is_active,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.ListReportsAsync(machine_id, report_type, is_active, cancellationToken));
        })
        .WithName("ListReports")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports", async (
            ReportCreateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.CreateReportAsync(request, cancellationToken));
        })
        .WithName("CreateReport")
        .RequireAuthorization("CanDownloadReports");

        app.MapPut("/api/reports/{id}", async (
            int id,
            ReportUpdateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return (await reportService.UpdateReportAsync(id, request, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateReport")
        .RequireAuthorization("CanDownloadReports");

        app.MapDelete("/api/reports/{id}", async (
            int id,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return (await reportService.DeleteReportAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteReport")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/generate", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.GenerateAsync(request, cancellationToken));
        })
        .WithName("GenerateReport")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/schedule", async (
            ReportScheduleRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.ScheduleAsync(request, cancellationToken));
        })
        .WithName("ScheduleReport")
        .RequireAuthorization("CanDownloadReports");

        app.MapGet("/api/reports/schedules", async (
            string? machine_id,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.ListSchedulesAsync(machine_id, cancellationToken));
        })
        .WithName("ListReportSchedules")
        .RequireAuthorization("CanDownloadReports");

        app.MapPut("/api/reports/schedules/{id:long}", async (
            long id,
            ReportScheduleUpdateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return (await reportService.UpdateScheduleAsync(id, request, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateReportSchedule")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/production/matrix", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.GetProductionMatrixAsync(request, cancellationToken));
        })
        .WithName("GetProductionReportMatrix")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/status/matrix", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.GetStatusMatrixAsync(request, cancellationToken));
        })
        .WithName("GetStatusReportMatrix")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/downtime/events", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.GetDowntimeEventsAsync(request, cancellationToken));
        })
        .WithName("GetDowntimeReportEvents")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/production/export/csv", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            var csv = await reportService.ExportProductionCsvAsync(request, cancellationToken);
            return Results.Text(csv, "text/csv");
        })
        .WithName("ExportProductionReportCsv")
        .RequireAuthorization("CanDownloadReports");

        app.MapPost("/api/reports/export/csv", async (
            ReportGenerateRequest request,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            var csv = await reportService.ExportCsvAsync(request, cancellationToken);
            return Results.Text(csv, "text/csv");
        })
        .WithName("ExportReportCsv")
        .RequireAuthorization("CanDownloadReports");

        app.MapGet("/api/reports/machine-dashboard", async (
            string machine_id,
            DateTime from,
            DateTime to,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.GetMachineDashboardAsync(machine_id, from, to, cancellationToken));
        })
        .WithName("GetMachineReportDashboard");

        app.MapGet("/api/reports/executions", async (
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await reportService.ListExecutionsAsync(cancellationToken));
        })
        .WithName("ListReportExecutions")
        .RequireAuthorization("CanDownloadReports");

        app.MapDelete("/api/reports/executions/{id:long}", async (
            long id,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            return (await reportService.DeleteExecutionAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteReportExecution")
        .RequireAuthorization("CanDownloadReports");

        return app;
    }
}
