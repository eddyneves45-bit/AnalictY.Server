using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class OeeApplicationService : IOeeApplicationService
{
    private readonly ScadaDbContext _dbContext;
    private readonly ISystemTimeService _timeService;

    public OeeApplicationService(ScadaDbContext dbContext, ISystemTimeService timeService)
    {
        _dbContext = dbContext;
        _timeService = timeService;
    }

    public async Task<object> GetResolvedStatesAsync(CancellationToken cancellationToken = default)
    {
        var machines = await _dbContext.Machines.Where(m => m.IsActive).ToListAsync(cancellationToken);
        var machineStates = await _dbContext.MachineStates.ToListAsync(cancellationToken);
        var effectiveStatuses = await GetCurrentMesStatusesAsync(machines.Select(machine => machine.Id.ToString()), cancellationToken);

        var machinesData = machines.Select(m =>
        {
            var state = machineStates.FirstOrDefault(s => s.MachineId == m.Id.ToString());
            var statusValue = effectiveStatuses.GetValueOrDefault(m.Id.ToString(), NormalizeMachineStatus(state?.State));

            return new
            {
                machine_id = m.Id.ToString(),
                resolved_state = new { machine_status = statusValue }
            };
        }).ToList();

        return new { machines = machinesData };
    }

    public async Task<object> GetAllMachinesOeeAsync(CancellationToken cancellationToken = default)
    {
        var machineStates = await _dbContext.MachineStates.ToListAsync(cancellationToken);
        var machines = await _dbContext.Machines.ToListAsync(cancellationToken);
        var oeeConfigs = await _dbContext.MachineOEEConfigs.ToListAsync(cancellationToken);
        var mesSummaries = await GetTodayMesSummariesAsync(machines.Select(machine => machine.Id.ToString()).ToList(), cancellationToken);

        var machinesOee = machines.Select(m =>
        {
            var state = machineStates.FirstOrDefault(s => s.MachineId == m.Id.ToString());
            var config = oeeConfigs.FirstOrDefault(c => c.MachineId == m.Id.ToString());
            var mes = mesSummaries.GetValueOrDefault(m.Id.ToString());
            var metrics = mes == null ? CalculateOee(state, config) : CalculateMesOee(mes, config);

            return new
            {
                machine_id = m.Id.ToString(),
                name = m.Name,
                code = m.Code,
                state = state?.State ?? "UNKNOWN",
                oee = metrics.oee,
                availability = metrics.availability,
                performance = metrics.performance,
                quality = metrics.quality,
                production_count = mes?.Production ?? state?.ProductionCount ?? 0
            };
        }).ToList();

        return new { machines = machinesOee, count = machinesOee.Count };
    }

    public async Task<object?> GetMachineOeeAsync(string machineId, CancellationToken cancellationToken = default)
    {
        var machine = await _dbContext.Machines.FindAsync(new object[] { int.Parse(machineId) }, cancellationToken);
        if (machine == null)
        {
            return null;
        }

        var machineState = await _dbContext.MachineStates.FirstOrDefaultAsync(s => s.MachineId == machineId, cancellationToken);
        var config = await _dbContext.MachineOEEConfigs.FirstOrDefaultAsync(c => c.MachineId == machineId, cancellationToken);
        var mesSummary = (await GetTodayMesSummariesAsync([machineId], cancellationToken)).GetValueOrDefault(machineId);
        var metrics = mesSummary == null ? CalculateOee(machineState, config) : CalculateMesOee(mesSummary, config);

        return new
        {
            machine_id = machineId,
            name = machine.Name,
            code = machine.Code,
            state = machineState?.State ?? "UNKNOWN",
            oee = metrics.oee,
            availability = metrics.availability,
            performance = metrics.performance,
            quality = metrics.quality,
            production_count = mesSummary?.Production ?? machineState?.ProductionCount ?? 0,
            time_running = mesSummary?.ProductionSeconds ?? machineState?.TimeRunning ?? 0.0,
            time_stopped = mesSummary == null ? machineState?.TimeStopped ?? 0.0 : mesSummary.IdleSeconds + mesSummary.MaintenanceSeconds + mesSummary.InactiveSeconds,
            time_fault = machineState?.TimeFault ?? 0.0,
            time_setup = machineState?.TimeSetup ?? 0.0,
            ideal_speed = config?.IdealSpeed ?? 0.0
        };
    }

    public async Task<object> GetMachineStopsAsync(string machineId, int limit, CancellationToken cancellationToken = default)
    {
        var stops = await _dbContext.StopEvents
            .Where(s => s.MachineId == machineId)
            .OrderByDescending(s => s.StartTime)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new
        {
            machine_id = machineId,
            stops = stops.Select(s => new
            {
                id = s.Id,
                machine_id = s.MachineId,
                start_time = s.StartTime,
                end_time = s.EndTime,
                duration = s.Duration,
                stop_type = s.StopType,
                cause = s.Cause,
                reason = s.Reason,
                cause_type = s.CauseType,
                confidence = s.Confidence,
                evidence = s.Evidence
            }),
            count = stops.Count
        };
    }

    public async Task<object> SetIdealSpeedAsync(IdealSpeedRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetOrCreateOeeConfigAsync(request.machine_id, cancellationToken);
        config.IdealSpeed = request.speed;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new
        {
            machine_id = request.machine_id,
            ideal_speed = request.speed,
            message = "Velocidade ideal definida com sucesso"
        };
    }

    public async Task<object> SetQualityAsync(QualityRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetOrCreateOeeConfigAsync(request.machine_id, cancellationToken);
        config.Quality = request.quality;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new
        {
            machine_id = request.machine_id,
            quality = request.quality,
            message = "Qualidade definida com sucesso"
        };
    }

    public async Task<object> SetStopThresholdsAsync(StopThresholdsRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetOrCreateOeeConfigAsync(request.machine_id, cancellationToken);
        config.MicroStopThreshold = request.micro_stop_threshold;
        config.LongStopThreshold = request.long_stop_threshold;
        config.NoDataThreshold = request.no_data_threshold;
        config.IncludeMicroStopsInOEE = request.include_micro_stops_in_oee;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new
        {
            machine_id = request.machine_id,
            micro_stop_threshold = request.micro_stop_threshold,
            long_stop_threshold = request.long_stop_threshold,
            no_data_threshold = request.no_data_threshold,
            include_micro_stops_in_oee = request.include_micro_stops_in_oee,
            message = "Thresholds definidos com sucesso"
        };
    }

    private async Task<MachineOEEConfig> GetOrCreateOeeConfigAsync(string machineId, CancellationToken cancellationToken)
    {
        var config = await _dbContext.MachineOEEConfigs.FirstOrDefaultAsync(c => c.MachineId == machineId, cancellationToken);
        if (config != null)
        {
            return config;
        }

        config = new MachineOEEConfig
        {
            MachineId = machineId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.MachineOEEConfigs.Add(config);

        return config;
    }

    private static (double availability, double performance, double quality, double oee) CalculateOee(
        MachineState? state,
        MachineOEEConfig? config)
    {
        var availability = 0.0;
        var performance = 0.0;
        var quality = config?.Quality ?? 1.0;
        var oee = 0.0;

        if (state != null && state.TotalTime > 0)
        {
            availability = state.TimeRunning / state.TotalTime;
            if (config != null && config.IdealSpeed > 0)
            {
                performance = state.AverageSpeed / config.IdealSpeed;
            }
            oee = availability * performance * quality;
        }

        return (availability, performance, quality, oee);
    }

    private async Task<Dictionary<string, MesOeeSummary>> GetTodayMesSummariesAsync(
        IReadOnlyList<string> machineIds,
        CancellationToken cancellationToken)
    {
        if (machineIds.Count == 0)
        {
            return [];
        }

        var config = await _dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(item => item.IsActive && item.Provider != "SQLServer")
            .OrderByDescending(item => item.IsPrimary)
            .ThenByDescending(item => item.IsLocal)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (config == null)
        {
            return [];
        }

        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var utcFrom = _timeService.LocalToUtc(localNow.Date, timeZone);
        var utcTo = _timeService.LocalToUtc(localNow.Date.AddDays(1), timeZone);

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        var production = await QueryProductionAsync(connection, "eventos_producao", machineIds, utcFrom, utcTo, cancellationToken);
        var losses = await QueryProductionAsync(connection, "eventos_perda", machineIds, utcFrom, utcTo, cancellationToken);
        var status = await QueryStatusAsync(connection, machineIds, utcFrom, utcTo, cancellationToken);

        return machineIds.ToDictionary(
            machineId => machineId,
            machineId =>
            {
                status.TryGetValue(machineId, out var statusValues);
                return new MesOeeSummary(
                    production.GetValueOrDefault(machineId),
                    losses.GetValueOrDefault(machineId),
                    statusValues?.GetValueOrDefault(1) ?? 0,
                    statusValues?.GetValueOrDefault(2) ?? 0,
                    statusValues?.GetValueOrDefault(3) ?? 0,
                    statusValues?.GetValueOrDefault(0) ?? 0);
            });
    }

    private static async Task<Dictionary<string, double>> QueryProductionAsync(
        MySqlConnection connection,
        string tableName,
        IReadOnlyList<string> machineIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var parameters = machineIds.Select((_, index) => $"@machine_{index}").ToArray();
        command.CommandText = $"""
            SELECT id_maquina, COALESCE(SUM(quantidade), 0)
            FROM {tableName}
            WHERE id_maquina IN ({string.Join(", ", parameters)})
              AND ocorrido_em >= @from
              AND ocorrido_em < @to
            GROUP BY id_maquina
            """;
        for (var index = 0; index < machineIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], machineIds[index]);
        }
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var values = new Dictionary<string, double>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetDouble(1);
        }
        return values;
    }

    private static async Task<Dictionary<string, Dictionary<int, double>>> QueryStatusAsync(
        MySqlConnection connection,
        IReadOnlyList<string> machineIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var parameters = machineIds.Select((_, index) => $"@machine_{index}").ToArray();
        command.CommandText = $"""
            SELECT id_maquina, status_maquina,
                   COALESCE(SUM(TIMESTAMPDIFF(MICROSECOND, GREATEST(inicio_em, @from), LEAST(COALESCE(fim_em, @to), @to)) / 1000000), 0)
            FROM eventos_status_maquina
            WHERE id_maquina IN ({string.Join(", ", parameters)})
              AND inicio_em < @to
              AND COALESCE(fim_em, @to) > @from
            GROUP BY id_maquina, status_maquina
            """;
        for (var index = 0; index < machineIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], machineIds[index]);
        }
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var values = machineIds.ToDictionary(machineId => machineId, _ => new Dictionary<int, double>());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)][reader.GetInt32(1)] = reader.GetDouble(2);
        }
        return values;
    }

    private static (double availability, double performance, double quality, double oee) CalculateMesOee(
        MesOeeSummary summary,
        MachineOEEConfig? config)
    {
        var totalSeconds = summary.ProductionSeconds + summary.IdleSeconds + summary.MaintenanceSeconds + summary.InactiveSeconds;
        var availability = totalSeconds > 0 ? summary.ProductionSeconds / totalSeconds : 0;
        var quality = summary.Production > 0 ? Math.Max(summary.Production - summary.Losses, 0) / summary.Production : config?.Quality ?? 1.0;
        var idealProduction = config?.IdealSpeed is > 0 ? config.IdealSpeed * (summary.ProductionSeconds / 3600.0) : 0;
        var performance = idealProduction > 0 ? summary.Production / idealProduction : 0;
        var oee = availability * performance * quality;
        return (availability, performance, quality, oee);
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
            MaximumPoolSize = (uint)Math.Max(config.PoolSize, 1),
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5
        }.ConnectionString;

    private async Task<Dictionary<string, int>> GetCurrentMesStatusesAsync(IEnumerable<string> machineIds, CancellationToken cancellationToken)
    {
        var ids = machineIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var config = await _dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(item => item.IsActive && item.Provider != "SQLServer")
            .OrderByDescending(item => item.IsPrimary)
            .ThenByDescending(item => item.IsLocal)
            .ThenBy(item => item.Id)
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
                command.Parameters.AddWithValue(parameter.name, parameter.id);
            }

            var statuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var machineId = reader.GetString(0);
                if (statuses.ContainsKey(machineId))
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
            _ when string.Equals(value.ToString(), "RUNNING", StringComparison.OrdinalIgnoreCase) => 1,
            _ when string.Equals(value.ToString(), "OPERACAO", StringComparison.OrdinalIgnoreCase) => 1,
            _ when string.Equals(value.ToString(), "OPERAÇÃO", StringComparison.OrdinalIgnoreCase) => 1,
            _ when string.Equals(value.ToString(), "IDLE", StringComparison.OrdinalIgnoreCase) => 2,
            _ when string.Equals(value.ToString(), "OCIOSA", StringComparison.OrdinalIgnoreCase) => 2,
            _ when string.Equals(value.ToString(), "MAINTENANCE", StringComparison.OrdinalIgnoreCase) => 3,
            _ when string.Equals(value.ToString(), "MANUTENCAO", StringComparison.OrdinalIgnoreCase) => 3,
            _ when string.Equals(value.ToString(), "MANUTENÇÃO", StringComparison.OrdinalIgnoreCase) => 3,
            _ => 0
        };
    }

    private sealed record MesOeeSummary(
        double Production,
        double Losses,
        double ProductionSeconds,
        double IdleSeconds,
        double MaintenanceSeconds,
        double InactiveSeconds);
}
