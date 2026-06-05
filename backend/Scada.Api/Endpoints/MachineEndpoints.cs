using Scada.Api.Services;

public static class MachineEndpoints
{
    public static WebApplication MapMachineEndpoints(this WebApplication app)
    {
        app.MapGet("/api/machines", async (
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await machineService.GetMachinesAsync(cancellationToken));
        })
        .WithName("GetMachines");

        app.MapGet("/api/machine-folders", async (
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await machineService.GetFoldersAsync(cancellationToken));
        })
        .WithName("GetMachineFolders");

        app.MapPost("/api/machine-folders", async (
            HttpContext context,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineFolderRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await machineService.CreateFolderAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateMachineFolder");

        app.MapPut("/api/machine-folders/{id}", async (
            int id,
            HttpContext context,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineFolderRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await machineService.UpdateFolderAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateMachineFolder");

        app.MapDelete("/api/machine-folders/{id}", async (
            int id,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            return (await machineService.DeleteFolderAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteMachineFolder");

        app.MapGet("/api/machines/{id}", async (
            int id,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            return (await machineService.GetMachineAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("GetMachineById");

        app.MapPost("/api/machines", async (
            HttpContext context,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await machineService.CreateMachineAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateMachine");

        app.MapPut("/api/machines/{id}", async (
            int id,
            HttpContext context,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await machineService.UpdateMachineAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateMachine");

        app.MapDelete("/api/machines/{id}", async (
            int id,
            IMachineService machineService,
            CancellationToken cancellationToken) =>
        {
            return (await machineService.DeleteMachineAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteMachine");

        app.MapGet("/api/machines/{id}/goals", async (
            int id,
            IMachineGoalService machineGoalService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await machineGoalService.ListAsync(id, cancellationToken));
        })
        .WithName("GetMachineGoals");

        app.MapPost("/api/machines/{id}/goals", async (
            int id,
            HttpContext context,
            IMachineGoalService machineGoalService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineGoalRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });

            return (await machineGoalService.CreateAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateMachineGoal")
        .RequireAuthorization("CanManageGoals");

        return app;
    }
}
