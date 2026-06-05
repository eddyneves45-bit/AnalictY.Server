using Scada.Api.Services;

public static class DowntimeEndpoints
{
    public static WebApplication MapDowntimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/downtimes", async (
            string? machine_id,
            DateTime? from,
            DateTime? to,
            int? limit,
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(machine_id, from, to, limit ?? 30, cancellationToken)));

        app.MapGet("/api/downtimes/retention", async (
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetRetentionAsync(cancellationToken)));

        app.MapPut("/api/downtimes/retention", async (
            DowntimeRetentionRequest request,
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            (await service.SetRetentionAsync(request, cancellationToken)).ToHttpResult());

        app.MapGet("/api/downtime-reasons/catalog", async (
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListReasonsAsync(cancellationToken)));

        app.MapPost("/api/downtime-reasons/catalog", async (
            DowntimeReasonCreateRequest request,
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateReasonAsync(request, cancellationToken)));

        app.MapPost("/api/downtimes/{id:long}/classify", async (
            long id,
            DowntimeClassifyRequest request,
            IDowntimeService service,
            CancellationToken cancellationToken) =>
            (await service.ClassifyAsync(id, request, cancellationToken)).ToHttpResult());

        return app;
    }
}
