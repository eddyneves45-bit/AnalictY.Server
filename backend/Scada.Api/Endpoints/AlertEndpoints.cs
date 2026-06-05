using Scada.Api.Services;

public static class AlertEndpoints
{
    public static WebApplication MapAlertEndpoints(this WebApplication app)
    {
        app.MapGet("/api/alerts", async (
            string? machine_id,
            string? alert_type,
            string? severity,
            bool? is_acknowledged,
            int? limit,
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await alertService.ListAlertsAsync(
                machine_id,
                alert_type,
                severity,
                is_acknowledged,
                limit ?? 20,
                cancellationToken));
        })
        .WithName("ListAlerts")
        .RequireAuthorization("CanManageAlertRules");

        app.MapGet("/api/alerts/retention", async (
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await alertService.GetRetentionAsync(cancellationToken));
        })
        .WithName("GetAlertRetention")
        .RequireAuthorization("CanManageAlertRules");

        app.MapPut("/api/alerts/retention", async (
            AlertRetentionRequest request,
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return (await alertService.SetRetentionAsync(request, cancellationToken)).ToHttpResult();
        })
        .WithName("SetAlertRetention")
        .RequireAuthorization("CanManageAlertRules");

        app.MapPost("/api/alerts", async (
            AlertCreateRequest request,
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await alertService.CreateAlertAsync(request, cancellationToken));
        })
        .WithName("CreateAlert")
        .RequireAuthorization("CanManageAlertRules");

        app.MapPost("/api/alerts/{id}/acknowledge", async (
            int id,
            string acknowledged_by,
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return (await alertService.AcknowledgeAlertAsync(id, acknowledged_by, cancellationToken)).ToHttpResult();
        })
        .WithName("AcknowledgeExistingAlert")
        .RequireAuthorization("CanManageAlertRules");

        app.MapDelete("/api/alerts/{id}", async (
            int id,
            IAlertService alertService,
            CancellationToken cancellationToken) =>
        {
            return (await alertService.DeleteAlertAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteAlert")
        .RequireAuthorization("CanManageAlertRules");

        return app;
    }
}
