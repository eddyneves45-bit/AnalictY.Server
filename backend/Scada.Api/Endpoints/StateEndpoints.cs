using Scada.Api.Services;

public static class StateEndpoints
{
    public static WebApplication MapStateEndpoints(this WebApplication app)
    {
        app.MapPost("/api/state/register/{machineId}", (string machineId, IStateService stateService) =>
        {
            return Results.Ok(stateService.RegisterMachine(machineId));
        })
        .WithName("RegisterMachine");

        app.MapPost("/api/state/input/{machineId}", async (string machineId, int statusWord, IStateService stateService) =>
        {
            return Results.Ok(await stateService.ProcessInputAsync(machineId, statusWord));
        })
        .WithName("ProcessStateInput");

        app.MapGet("/api/state/machines", (IStateService stateService) =>
        {
            return Results.Ok(stateService.GetAllMachineStates());
        })
        .WithName("GetAllMachinesState");

        app.MapGet("/api/state/machines/{machineId}", (string machineId, IStateService stateService) =>
        {
            return stateService.GetMachineState(machineId).ToHttpResult();
        })
        .WithName("GetMachineState");

        app.MapGet("/api/quality/health", (IStateService stateService) =>
        {
            return Results.Ok(stateService.GetConnectionHealth());
        })
        .WithName("GetConnectionHealth");

        app.MapGet("/api/quality/health/{connectionId}", (string connectionId, IStateService stateService) =>
        {
            return Results.Ok(stateService.GetConnectionHealth(connectionId));
        })
        .WithName("GetConnectionHealthById");

        return app;
    }
}
