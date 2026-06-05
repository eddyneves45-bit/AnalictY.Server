using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Gateway.Interfaces;
using DataScadaDbContext = Scada.Data.Models.ScadaDbContext;

namespace Scada.Api.Data;

internal static class RuntimeTagSeeder
{
    public static async Task RegisterConfiguredTagsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataScadaDbContext>();
        var runtimeService = scope.ServiceProvider.GetRequiredService<ITagRuntimeService>();
        var heartbeatService = scope.ServiceProvider.GetRequiredService<IIndustrialHeartbeatService>();
        var snapshotStore = scope.ServiceProvider.GetRequiredService<ITagRuntimeSnapshotStore>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RuntimeTagSeeder");

        var tags = await dbContext.TagConfigs
            .Where(tag => tag.IsActive)
            .OrderBy(tag => tag.Id)
            .ToListAsync();
        var snapshots = await snapshotStore.LoadAsync();

        foreach (var tag in tags)
        {
            await runtimeService.RegisterTagAsync(
                tag.Id,
                tag.TagName,
                tag.Address,
                tag.DriverType,
                tag.DataType,
                tag.PollIntervalMs);
            heartbeatService.RegisterTag(
                tag.Id,
                tag.TagName,
                tag.DriverType,
                tag.MqttConnectionId ?? tag.OpcuaConnectionId,
                tag.PollIntervalMs);

            if (snapshots.TryGetValue(tag.Id, out var snapshot))
            {
                runtimeService.UpdateTagConnectionStatus(tag.Id, false);
                runtimeService.UpdateTagValue(tag.Id, snapshot.Value, "STALE");
            }
        }

        logger.LogInformation("{Count} tags ativas registradas no runtime", tags.Count);
    }
}
