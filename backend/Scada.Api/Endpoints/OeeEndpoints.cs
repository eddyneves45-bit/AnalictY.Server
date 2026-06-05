using Scada.Api.Services;

public static class OeeEndpoints
{
    public static WebApplication MapOeeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/machines/resolved-state-all", async (
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.GetResolvedStatesAsync(cancellationToken));
        })
        .WithName("GetMachinesResolvedStateAll");

        app.MapGet("/api/machines/oee", async (
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.GetAllMachinesOeeAsync(cancellationToken));
        })
        .WithName("GetAllMachinesOee");

        app.MapGet("/api/machines/oee/{machineId}", async (
            string machineId,
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            var result = await oeeService.GetMachineOeeAsync(machineId, cancellationToken);
            return result != null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("GetMachineOee");

        app.MapGet("/api/machines/oee/{machineId}/stops", async (
            string machineId,
            int limit,
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.GetMachineStopsAsync(machineId, limit, cancellationToken));
        })
        .WithName("GetMachineStops");

        app.MapPost("/api/machines/oee/ideal-speed", async (
            IdealSpeedRequest request,
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.SetIdealSpeedAsync(request, cancellationToken));
        })
        .WithName("SetIdealSpeed");

        app.MapPost("/api/machines/oee/quality", async (
            QualityRequest request,
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.SetQualityAsync(request, cancellationToken));
        })
        .WithName("SetQuality");

        app.MapPost("/api/machines/oee/stop-thresholds", async (
            StopThresholdsRequest request,
            IOeeApplicationService oeeService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await oeeService.SetStopThresholdsAsync(request, cancellationToken));
        })
        .WithName("SetStopThresholds");

        return app;
    }
}
