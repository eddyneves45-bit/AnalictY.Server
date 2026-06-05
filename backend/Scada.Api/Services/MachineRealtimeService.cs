using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Api.Realtime;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class MachineRealtimeService : IMachineRealtimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IHubContext<MesHub> _hubContext;

    public MachineRealtimeService(
        IServiceScopeFactory scopeFactory,
        ITagRuntimeService tagRuntimeService,
        IHubContext<MesHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _tagRuntimeService = tagRuntimeService;
        _hubContext = hubContext;
    }

    public async Task<IReadOnlyList<MachineRealtimeState>> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var machines = await dbContext.Machines
            .AsNoTracking()
            .Where(machine => machine.IsActive)
            .ToListAsync(cancellationToken);
        var mappings = await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map => map.IsActive && map.TagAlias == "machine_status")
            .ToListAsync(cancellationToken);
        var effectiveStatuses = await LoadEffectiveStatusesAsync(dbContext, machines.Select(machine => machine.Id), cancellationToken);

        return machines.Select(machine =>
        {
            var mapping = mappings.FirstOrDefault(item => item.MachineId == machine.Id.ToString());
            var value = mapping == null ? null : _tagRuntimeService.GetTagState(mapping.TagConfigId)?.Value;
            var runtimeStatus = NormalizeMachineStatus(value);
            return new MachineRealtimeState(
                machine.Id,
                new { machine_status = value == null ? effectiveStatuses.GetValueOrDefault(machine.Id, runtimeStatus) : runtimeStatus });
        }).ToList();
    }

    public async Task PublishFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var mappings = await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map =>
                map.IsActive &&
                map.TagConfigId == envelope.TagId &&
                map.TagAlias == "machine_status")
            .ToListAsync(cancellationToken);
        var machineIds = mappings
            .Select(map => map.MachineId)
            .Where(machineId => int.TryParse(machineId, out _))
            .Select(int.Parse)
            .ToList();
        var effectiveStatuses = await LoadEffectiveStatusesAsync(dbContext, machineIds, cancellationToken);

        foreach (var machineId in machineIds)
        {
            await _hubContext.Clients.All.SendAsync(
                "machines:update",
                new MachineRealtimeState(
                    machineId,
                    new { machine_status = NormalizeMachineStatus(envelope.Value) }),
                cancellationToken);
        }
    }

    public async Task PublishEffectiveFromTagAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var machineIds = await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map => map.IsActive && map.TagConfigId == envelope.TagId)
            .Select(map => map.MachineId)
            .ToListAsync(cancellationToken);
        var parsedMachineIds = machineIds
            .Where(machineId => int.TryParse(machineId, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();
        if (parsedMachineIds.Count == 0)
        {
            return;
        }

        var effectiveStatuses = await LoadEffectiveStatusesAsync(dbContext, parsedMachineIds, cancellationToken);
        foreach (var machineId in parsedMachineIds)
        {
            if (!effectiveStatuses.TryGetValue(machineId, out var effectiveStatus))
            {
                continue;
            }

            await _hubContext.Clients.All.SendAsync(
                "machines:update",
                new MachineRealtimeState(machineId, new { machine_status = effectiveStatus }),
                cancellationToken);
        }
    }

    private static async Task<Dictionary<int, int>> LoadEffectiveStatusesAsync(
        ScadaDbContext dbContext,
        IEnumerable<int> machineIds,
        CancellationToken cancellationToken)
    {
        var ids = machineIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var config = await dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (config == null)
        {
            return [];
        }

        try
        {
            await using var connection = new MySqlConnection(BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            var parameters = ids.Select((id, index) => new { id, name = $"@m{index}" }).ToList();
            command.CommandText = $"""
                SELECT id_maquina, status_maquina
                FROM eventos_status_maquina
                WHERE fim_em IS NULL
                  AND id_maquina IN ({string.Join(", ", parameters.Select(item => item.name))})
                ORDER BY inicio_em DESC
                """;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.name, parameter.id.ToString());
            }

            var statuses = new Dictionary<int, int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!int.TryParse(reader.GetString(0), out var machineId) || statuses.ContainsKey(machineId))
                {
                    continue;
                }

                statuses[machineId] = NormalizeMachineStatus(reader.GetInt32(1));
            }

            return statuses;
        }
        catch
        {
            return [];
        }
    }

    private static string BuildConnectionString(MySqlConfig config) =>
        new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            UserID = config.User,
            Password = config.Password,
            Database = config.Database,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = (uint)Math.Max(config.PoolSize, 1),
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5
        }.ConnectionString;

    private static int NormalizeMachineStatus(object? value)
    {
        return value switch
        {
            null => 0,
            int typed => typed is >= 0 and <= 3 ? typed : 0,
            long typed => typed is >= 0 and <= 3 ? (int)typed : 0,
            double typed => typed is >= 0 and <= 3 ? (int)typed : 0,
            float typed => typed is >= 0 and <= 3 ? (int)typed : 0,
            decimal typed => typed is >= 0 and <= 3 ? (int)typed : 0,
            _ when int.TryParse(value.ToString(), out var parsed) && parsed is >= 0 and <= 3 => parsed,
            _ => 0
        };
    }
}
