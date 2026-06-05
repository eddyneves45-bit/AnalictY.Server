using Scada.Api.Services;

public static class AlertRuleEndpoints
{
    public static WebApplication MapAlertRuleEndpoints(this WebApplication app)
    {
        app.MapGet("/api/alert-rules", async (
            IAlertRuleService alertRuleService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await alertRuleService.ListAsync(cancellationToken));
        })
        .RequireAuthorization("CanManageAlertRules");

        app.MapPost("/api/alert-rules", async (
            AlertRuleRequest request,
            IAlertRuleService alertRuleService,
            CancellationToken cancellationToken) =>
        {
            return (await alertRuleService.CreateAsync(request, cancellationToken)).ToHttpResult();
        })
        .RequireAuthorization("CanManageAlertRules");

        app.MapPut("/api/alert-rules/{id:int}", async (
            int id,
            AlertRuleRequest request,
            IAlertRuleService alertRuleService,
            CancellationToken cancellationToken) =>
        {
            return (await alertRuleService.UpdateAsync(id, request, cancellationToken)).ToHttpResult();
        })
        .RequireAuthorization("CanManageAlertRules");

        app.MapDelete("/api/alert-rules/{id:int}", async (
            int id,
            IAlertRuleService alertRuleService,
            CancellationToken cancellationToken) =>
        {
            return (await alertRuleService.DeleteAsync(id, cancellationToken)).ToHttpResult();
        })
        .RequireAuthorization("CanManageAlertRules");

        return app;
    }
}
