using Scada.Api.Services;

public static class TagHistoryEndpoints
{
    public static WebApplication MapTagHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/history/tags", async (
            int? tag_id,
            string? tag_name,
            DateTime? from,
            DateTime? to,
            int? limit,
            ITagHistoryStore historyStore,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await historyStore.QueryAsync(tag_id, tag_name, from, to, limit ?? 500, cancellationToken));
        })
        .WithName("GetTagHistory");

        return app;
    }
}
