using Scada.Api.Services;

public static class LogEndpoints
{
    public static WebApplication MapLogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logs/recent", (
            IRecentLogStore logStore,
            int? take,
            string? level,
            string? search) =>
        {
            return Results.Ok(logStore.GetRecent(take ?? 200, level, search));
        })
        .WithName("GetRecentLogs");

        return app;
    }
}
