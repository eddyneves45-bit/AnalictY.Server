using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

public static class DashboardEndpoints
{
    public static WebApplication MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard/overview", async (
            IDashboardService dashboardService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await dashboardService.GetOverviewAsync(cancellationToken));
        })
        .WithName("GetDashboardOverview");

        app.MapGet("/api/dashboard/machines/{machineId}", async (
            string machineId,
            IDashboardService dashboardService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await dashboardService.GetMachineStatusAsync(machineId, cancellationToken));
        })
        .WithName("GetMachineStatus");

        app.MapGet("/api/dashboard/configs", async (
            string? machine_id,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = dbContext.DashboardConfigs.AsNoTracking().Where(item => item.IsActive);
            if (!string.IsNullOrWhiteSpace(machine_id))
            {
                query = query.Where(item => item.MachineId == machine_id);
            }

            var dashboardEntities = await query
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.Name)
                .ToListAsync(cancellationToken);

            return Results.Ok(dashboardEntities.Select(ToDashboardResponse).ToList());
        })
        .RequireAuthorization()
        .WithName("ListDashboardConfigs");

        app.MapGet("/api/dashboard/configs/default", async (
            string machine_id,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var dashboard = await dbContext.DashboardConfigs
                .AsNoTracking()
                .Where(item => item.IsActive && item.MachineId == machine_id)
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken);

            return dashboard is null
                ? Results.NotFound(new { message = "Nenhum dashboard configurado para esta máquina." })
                : Results.Ok(ToDashboardResponse(dashboard));
        })
        .RequireAuthorization()
        .WithName("GetDefaultDashboardConfig");

        app.MapPut("/api/dashboard/configs", async (
            DashboardConfigRequest request,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.name) || string.IsNullOrWhiteSpace(request.machine_id))
            {
                return Results.BadRequest(new { message = "Informe nome e máquina do dashboard." });
            }

            var widgetsJson = request.widgets.ValueKind == JsonValueKind.Undefined || request.widgets.ValueKind == JsonValueKind.Null
                ? "[]"
                : request.widgets.GetRawText();
            var now = DateTime.UtcNow;
            DashboardConfig config;
            if (request.id.HasValue)
            {
                config = await dbContext.DashboardConfigs.FirstOrDefaultAsync(item => item.Id == request.id.Value, cancellationToken)
                    ?? new DashboardConfig { CreatedAt = now };
            }
            else
            {
                config = new DashboardConfig { CreatedAt = now };
                dbContext.DashboardConfigs.Add(config);
            }

            config.Name = request.name.Trim();
            config.MachineId = request.machine_id.Trim();
            config.PeriodPreset = string.IsNullOrWhiteSpace(request.period_preset) ? "today" : request.period_preset.Trim();
            config.RefreshInterval = string.IsNullOrWhiteSpace(request.refresh_interval) ? "10" : request.refresh_interval.Trim();
            config.WidgetsJson = widgetsJson;
            config.IsDefault = request.is_default;
            config.IsActive = request.is_active ?? true;
            config.UpdatedAt = now;

            if (config.IsDefault)
            {
                var currentDefaults = await dbContext.DashboardConfigs
                    .Where(item => item.MachineId == config.MachineId && item.Id != config.Id && item.IsDefault)
                    .ToListAsync(cancellationToken);
                foreach (var item in currentDefaults)
                {
                    item.IsDefault = false;
                    item.UpdatedAt = now;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToDashboardResponse(config));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithName("UpsertDashboardConfig");

        app.MapDelete("/api/dashboard/configs/{id:int}", async (
            int id,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await dbContext.DashboardConfigs.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Dashboard não encontrado." });

            dbContext.DashboardConfigs.Remove(config);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithName("DeleteDashboardConfig");

        return app;
    }

    private static object ToDashboardResponse(DashboardConfig config) => new
    {
        config.Id,
        config.Name,
        config.MachineId,
        config.PeriodPreset,
        config.RefreshInterval,
        config.IsDefault,
        config.IsActive,
        config.CreatedAt,
        config.UpdatedAt,
        widgets = JsonSerializer.Deserialize<JsonElement>(config.WidgetsJson)
    };

    private sealed record DashboardConfigRequest(
        int? id,
        string name,
        string machine_id,
        string? period_preset,
        string? refresh_interval,
        bool is_default,
        bool? is_active,
        JsonElement widgets);
}
