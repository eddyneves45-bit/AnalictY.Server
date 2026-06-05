using Scada.Api.Services;

public static class IndustrialHealthEndpoints
{
    public static WebApplication MapIndustrialHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health/industrial", (
            ITagValueQueue queue,
            IIndustrialHeartbeatService heartbeatService) =>
        {
            return Results.Ok(new
            {
                queue = new
                {
                    approximate_count = queue.ApproximateCount,
                    enqueued = queue.EnqueuedCount,
                    dequeued = queue.DequeuedCount,
                    dropped = queue.DroppedCount
                },
                heartbeat = heartbeatService.GetSnapshot()
            });
        })
        .WithName("GetIndustrialHealth");

        app.MapGet("/api/health/connections", (IIndustrialHeartbeatService heartbeatService) =>
        {
            return Results.Ok(heartbeatService.GetSnapshot());
        })
        .WithName("GetIndustrialConnectionHealth");

        app.MapGet("/api/health/mysql", async (
            IMySqlPersistenceQueue mySqlPersistenceQueue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await mySqlPersistenceQueue.GetHealthAsync(cancellationToken));
        })
        .WithName("GetMySqlPersistenceHealth");

        return app;
    }
}
