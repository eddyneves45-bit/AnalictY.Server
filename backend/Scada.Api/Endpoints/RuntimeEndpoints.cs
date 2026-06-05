using Scada.Api.Services;

public static class RuntimeEndpoints
{
    public static WebApplication MapRuntimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/runtime/state", (IRuntimeService runtimeService) =>
        {
            return Results.Ok(runtimeService.GetRuntimeState());
        })
        .WithName("GetRuntimeState");

        return app;
    }
}
