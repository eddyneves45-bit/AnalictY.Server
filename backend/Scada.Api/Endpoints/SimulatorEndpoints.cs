using Scada.Api.Services;

public static class SimulatorEndpoints
{
    public static WebApplication MapSimulatorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/simulator/machines", async (
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await virtualMachineService.ListAsync(cancellationToken));
        })
        .WithName("GetVirtualMachines");

        app.MapPost("/api/simulator/machines", async (
            HttpContext context,
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<VirtualMachineCreateRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });
            return (await virtualMachineService.CreateAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateVirtualMachine");

        app.MapGet("/api/simulator/machines/{id:int}", async (
            int id,
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            return (await virtualMachineService.GetAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("GetVirtualMachineConsole");

        app.MapPost("/api/simulator/machines/{id:int}/publish", async (
            int id,
            HttpContext context,
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<VirtualMachineCommandRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });
            return (await virtualMachineService.PublishAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("PublishVirtualMachineValues");

        app.MapPost("/api/simulator/machines/{id:int}/start", async (
            int id,
            HttpContext context,
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<VirtualMachineStartRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });
            return (await virtualMachineService.StartAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("StartVirtualMachine");

        app.MapPost("/api/simulator/machines/{id:int}/stop", async (
            int id,
            IVirtualMachineService virtualMachineService,
            CancellationToken cancellationToken) =>
        {
            return (await virtualMachineService.StopAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("StopVirtualMachine");

        return app;
    }
}
