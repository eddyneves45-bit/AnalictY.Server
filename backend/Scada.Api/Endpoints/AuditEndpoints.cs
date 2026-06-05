using Microsoft.EntityFrameworkCore;
using Scada.Data.Models;

public static class AuditEndpoints
{
    public static WebApplication MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit/logs", async (
            int? take,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var logs = await dbContext.AuditLogs
                .AsNoTracking()
                .OrderByDescending(log => log.CreatedAt)
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .ToListAsync(cancellationToken);
            return Results.Ok(logs);
        })
        .RequireAuthorization("CanViewAudit");

        return app;
    }
}
