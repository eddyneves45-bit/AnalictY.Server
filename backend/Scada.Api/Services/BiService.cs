using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using System.Text;

namespace Scada.Api.Services;

internal class BiService : IBiService
{
    private readonly ScadaDbContext _dbContext;
    private readonly IMesSummaryService _mesSummaryService;
    private readonly ISystemTimeService _timeService;

    public BiService(ScadaDbContext dbContext, IMesSummaryService mesSummaryService, ISystemTimeService timeService)
    {
        _dbContext = dbContext;
        _mesSummaryService = mesSummaryService;
        _timeService = timeService;
    }

    public async Task<object> GetIndicatorsAsync(
        string? costCenter,
        string? machineId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var localTo = _timeService.NormalizeToLocal(toDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone), timeZone);
        var localFrom = _timeService.NormalizeToLocal(fromDate ?? localTo.AddDays(-7), timeZone);
        var window = _timeService.BuildWindow(localFrom, localTo, timeZone);
        var query = _dbContext.Machines.AsQueryable();

        if (!string.IsNullOrEmpty(costCenter))
            query = query.Where(m => m.CostCenter == costCenter);
        if (!string.IsNullOrEmpty(machineId))
            query = query.Where(m => m.Id.ToString() == machineId);

        var machines = await query.ToListAsync(cancellationToken);
        var machineIds = machines.Select(machine => machine.Id.ToString()).ToList();
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        Dictionary<string, RawProductionSummary> productions = [];
        Dictionary<string, StatusSummary> statuses = [];
        if (config != null && machineIds.Count > 0)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);
            productions = await GetProductionSummariesAsync(connection, machineIds, window.UtcFrom, window.UtcTo, cancellationToken);
            statuses = await GetStatusSummariesAsync(connection, machineIds, window.UtcFrom, window.UtcTo, cancellationToken);
        }

        var indicators = machines.Select(m =>
        {
            var id = m.Id.ToString();
            var rawProduction = productions.GetValueOrDefault(id, new RawProductionSummary(0, 0));
            var status = statuses.GetValueOrDefault(id, new StatusSummary(0, 0, 0, 0));
            var productionQuantity = rawProduction.total;
            int targetQuantity = 100;
            double efficiency = targetQuantity > 0 ? productionQuantity / targetQuantity : 0.0;
            double downtimeMinutes = (status.idle_seconds + status.maintenance_seconds + status.inactive_seconds) / 60.0;
            int downtimeCount = status.idle_seconds + status.maintenance_seconds + status.inactive_seconds > 0 ? 1 : 0;

            return new
            {
                cost_center = m.CostCenter,
                machine_id = m.Id,
                machine_name = m.Name,
                production_quantity = productionQuantity,
                target_quantity = targetQuantity,
                efficiency = efficiency,
                downtime_minutes = downtimeMinutes,
                downtime_count = downtimeCount,
                period_start = window.LocalFrom.ToString("o"),
                period_end = window.LocalTo.ToString("o")
            };
        }).ToList();

        return new { indicators, count = indicators.Count };
    }

    public async Task<object> GetCostCentersAsync(CancellationToken cancellationToken = default)
    {
        var costCenters = await _dbContext.Machines
            .Where(m => m.IsActive)
            .GroupBy(m => m.CostCenter)
            .Select(g => new
            {
                code = g.Key,
                machine_count = g.Count()
            })
            .ToListAsync(cancellationToken);

        return new { cost_centers = costCenters, count = costCenters.Count };
    }

    public async Task<object> GetMachinesAsync(string costCenter, CancellationToken cancellationToken = default)
    {
        var machines = await _dbContext.Machines
            .Where(m => m.IsActive && m.CostCenter == costCenter)
            .Select(m => new
            {
                id = m.Id,
                name = m.Name,
                code = m.Code,
                cost_center = m.CostCenter
            })
            .ToListAsync(cancellationToken);

        return new { machines, count = machines.Count };
    }

    public async Task<object> GetMachineOverviewAsync(string machineId, DateTime from, DateTime to, string? targetMode = null, CancellationToken cancellationToken = default)
    {
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(from, to, timeZone);
        var machine = await _dbContext.Machines
            .AsNoTracking()
            .Where(item => item.Id.ToString() == machineId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Code,
                item.CostCenter,
                item.Location
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (machine == null)
        {
            return new
            {
                machine = (object?)null,
                period = new { from = window.LocalFrom, to = window.LocalTo },
                goal = (object?)null,
                production = new { total = 0d, losses = 0d, good = 0d, attainment_percent = 0d },
                status = new { production_seconds = 0d, idle_seconds = 0d, maintenance_seconds = 0d, inactive_seconds = 0d },
                metrics = EmptyMetrics(),
                timeline = Array.Empty<object>()
            };
        }

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new
            {
                machine,
                period = new { from = window.LocalFrom, to = window.LocalTo },
                goal = (object?)null,
                production = new { total = 0d, losses = 0d, good = 0d, attainment_percent = 0d },
                status = new { production_seconds = 0d, idle_seconds = 0d, maintenance_seconds = 0d, inactive_seconds = 0d },
                metrics = EmptyMetrics(),
                timeline = Array.Empty<object>()
            };
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        await _mesSummaryService.RebuildMachineDayAsync(machineId, DateOnly.FromDateTime(window.LocalFrom), cancellationToken);
        if (DateOnly.FromDateTime(window.LocalTo) != DateOnly.FromDateTime(window.LocalFrom))
        {
            await _mesSummaryService.RebuildMachineDayAsync(machineId, DateOnly.FromDateTime(window.LocalTo), cancellationToken);
        }
        var goal = await GetCurrentActiveGoalAsync(connection, machineId, cancellationToken);
        var target = CalculateTarget(goal, window.LocalFrom, window.LocalTo, targetMode);
        var production = await GetProductionSummaryAsync(connection, machineId, window.UtcFrom, window.UtcTo, target, cancellationToken);
        var status = await GetStatusSummaryAsync(connection, machineId, window.UtcFrom, window.UtcTo, cancellationToken);
        var timeline = await GetHalfHourProductionTimelineAsync(connection, machineId, window.LocalFrom, window.LocalTo, window.UtcFrom, window.UtcTo, timeZone, goal?.meta_producao_hora, cancellationToken);
        var metrics = BuildMetrics(goal, production, status, target);

        return new
        {
            machine,
            period = new { from = window.LocalFrom, to = window.LocalTo },
            goal,
            production,
            status,
            metrics,
            timeline
        };
    }

    public async Task<object> GetMachineSummariesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(from, to, timeZone);
        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(machine => machine.IsActive)
            .Select(machine => machine.Id.ToString())
            .ToListAsync(cancellationToken);

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { period = new { from = window.LocalFrom, to = window.LocalTo }, items = Array.Empty<object>() };
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var goals = await GetCurrentActiveGoalsAsync(connection, machines, cancellationToken);
        var productions = await GetProductionSummariesAsync(connection, machines, window.UtcFrom, window.UtcTo, cancellationToken);
        var items = new List<object>();
        foreach (var machineId in machines)
        {
            goals.TryGetValue(machineId, out var goal);
            var target = CalculateTarget(goal, window.LocalFrom, window.LocalTo);
            var rawProduction = productions.GetValueOrDefault(machineId, new RawProductionSummary(0, 0));
            var production = BuildProductionSummary(rawProduction.total, rawProduction.losses, target);
            items.Add(new
            {
                machine_id = machineId,
                production_total = production.total,
                target,
                attainment_percent = production.attainment_percent
            });
        }

        return new { period = new { from = window.LocalFrom, to = window.LocalTo }, items };
    }

    public async Task<object> GetMachineProductionByShiftAsync(string machineId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { machine_id = machineId, date, items = Array.Empty<object>(), totals = EmptyShiftTotals() };
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        await _mesSummaryService.RebuildMachineDayAsync(machineId, date, cancellationToken);
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var goal = await GetCurrentActiveGoalAsync(connection, machineId, cancellationToken);
        var shifts = await GetActiveShiftsAsync(connection, cancellationToken);
        var items = new List<ShiftProductionSummary>();

        foreach (var shift in shifts)
        {
            var interval = BuildShiftInterval(date, shift.Start, shift.End);
            var production = await GetShiftProductionSummaryAsync(connection, machineId, date, shift.Id, cancellationToken);

            var shiftHours = (interval.End - interval.Start).TotalHours;
            var target = goal?.meta_producao_hora is > 0
                ? goal.meta_producao_hora.Value * shiftHours
                : goal?.meta_producao_dia is > 0
                    ? goal.meta_producao_dia.Value * shiftHours / 24
                    : 0;

            var status = await GetShiftStatusSummaryAsync(connection, machineId, date, shift.Id, cancellationToken);
            var total = status.production_seconds +
                        status.idle_seconds +
                        status.maintenance_seconds +
                        status.inactive_seconds;
            var availability = total > 0 ? status.production_seconds / total * 100 : 0;
            var totalProduction = production.total;
            var losses = production.losses;
            var good = production.good;
            var quality = totalProduction > 0 ? good / totalProduction * 100 : 0;
            var idealProduction = goal?.tempo_ciclo_ideal_segundos is > 0
                ? status.production_seconds / goal.tempo_ciclo_ideal_segundos.Value
                : 0;
            var performance = idealProduction > 0 ? totalProduction / idealProduction * 100 : 0;
            var oee = availability > 0 && performance > 0 && quality > 0
                ? availability * performance * quality / 10000
                : 0;

            items.Add(new ShiftProductionSummary(
                shift.Id,
                shift.Code,
                shift.Name,
                interval.Start,
                interval.End,
                target,
                totalProduction,
                losses,
                good,
                target > 0 ? totalProduction / target * 100 : 0,
                availability,
                performance,
                quality,
                oee));
        }

        var totalProductionSum = items.Sum(item => item.production);
        var totalLosses = items.Sum(item => item.losses);
        var totalGood = items.Sum(item => item.good);
        var totalTarget = items.Sum(item => item.target);

        return new
        {
            machine_id = machineId,
            date,
            goal,
            items,
            totals = new
            {
                production = totalProductionSum,
                losses = totalLosses,
                good = totalGood,
                target = totalTarget,
                attainment_percent = totalTarget > 0 ? totalProductionSum / totalTarget * 100 : 0
            }
        };
    }

    public async Task<string> ExportProductionCsvAsync(string? machineId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return "MachineId,OccurredAt,PreviousValue,CurrentValue,Quantity,TagId\n";
        }

        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var localTo = _timeService.NormalizeToLocal(toDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone), timeZone);
        var localFrom = _timeService.NormalizeToLocal(fromDate ?? localTo.Date, timeZone);
        var window = _timeService.BuildWindow(localFrom, localTo, timeZone);
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_maquina, ocorrido_em, valor_anterior, valor_atual, quantidade, id_tag_origem
            FROM eventos_producao
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND ocorrido_em >= @from
              AND ocorrido_em <= @to
            ORDER BY ocorrido_em
            """;
        command.Parameters.AddWithValue("@machine_id", string.IsNullOrWhiteSpace(machineId) ? DBNull.Value : machineId);
        command.Parameters.AddWithValue("@from", window.UtcFrom);
        command.Parameters.AddWithValue("@to", window.UtcTo);

        var csv = new StringBuilder("MachineId,OccurredAt,PreviousValue,CurrentValue,Quantity,TagId\n");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var localOccurredAt = _timeService.UtcToLocal(reader.GetDateTime(1), timeZone);
            csv.AppendLine($"{reader.GetString(0)},{localOccurredAt:O},{reader.GetDouble(2)},{reader.GetDouble(3)},{reader.GetDouble(4)},{reader.GetInt32(5)}");
        }
        return csv.ToString();
    }

    public async Task<string> ExportDowntimeCsvAsync(string? machineId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StopEvents.AsQueryable();

        if (!string.IsNullOrEmpty(machineId))
            query = query.Where(s => s.MachineId == machineId);
        if (fromDate.HasValue)
            query = query.Where(s => s.StartTime >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(s => s.StartTime <= toDate.Value);

        var stopEvents = await query.OrderByDescending(s => s.StartTime).ToListAsync(cancellationToken);
        var csv = new StringBuilder("Id,MachineId,StartTime,EndTime,Duration,StopType,Cause,Reason,CauseType,Confidence\n");
        foreach (var stop in stopEvents)
        {
            csv.Append($"{stop.Id},{stop.MachineId},{stop.StartTime:O},{stop.EndTime:O},{stop.Duration},{stop.StopType},{stop.Cause},{stop.Reason},{stop.CauseType},{stop.Confidence}\n");
        }

        return csv.ToString();
    }

    private async Task<MySqlConfig?> GetPrimaryMySqlConfigAsync(CancellationToken cancellationToken) =>
        await _dbContext.MySqlConfigs.AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<MachineGoalSnapshot?> GetCurrentActiveGoalAsync(MySqlConnection connection, string machineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, meta_producao_dia, meta_producao_hora, tempo_ciclo_ideal_segundos,
                   vigente_de, vigente_ate
            FROM metas_maquina
            WHERE id_maquina = @id_maquina
              AND ativo = TRUE
            ORDER BY vigente_de DESC, id DESC
            LIMIT 1
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MachineGoalSnapshot(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5));
    }

    private static async Task<Dictionary<string, MachineGoalSnapshot>> GetCurrentActiveGoalsAsync(
        MySqlConnection connection,
        IReadOnlyList<string> machineIds,
        CancellationToken cancellationToken)
    {
        if (machineIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        var parameters = machineIds.Select((_, index) => $"@machine_{index}").ToArray();
        command.CommandText = $"""
            SELECT id_maquina, id, meta_producao_dia, meta_producao_hora, tempo_ciclo_ideal_segundos,
                   vigente_de, vigente_ate
            FROM (
                SELECT id_maquina, id, meta_producao_dia, meta_producao_hora, tempo_ciclo_ideal_segundos,
                       vigente_de, vigente_ate,
                       ROW_NUMBER() OVER (PARTITION BY id_maquina ORDER BY vigente_de DESC, id DESC) AS rn
                FROM metas_maquina
                WHERE id_maquina IN ({string.Join(", ", parameters)})
                  AND ativo = TRUE
            ) goals
            WHERE rn = 1
            """;
        for (var index = 0; index < machineIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], machineIds[index]);
        }

        var goals = new Dictionary<string, MachineGoalSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            goals[reader.GetString(0)] = new MachineGoalSnapshot(
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6));
        }

        return goals;
    }

    private static async Task<ProductionSummary> GetProductionSummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateTime from,
        DateTime to,
        double target,
        CancellationToken cancellationToken)
    {
        var total = await SumQuantityAsync(connection, "eventos_producao", machineId, from, to, cancellationToken);
        var losses = await SumQuantityAsync(connection, "eventos_perda", machineId, from, to, cancellationToken);
        return BuildProductionSummary(total, losses, target);
    }

    private static async Task<Dictionary<string, RawProductionSummary>> GetProductionSummariesAsync(
        MySqlConnection connection,
        IReadOnlyList<string> machineIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        if (machineIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        var parameters = machineIds.Select((_, index) => $"@machine_{index}").ToArray();
        command.CommandText = $"""
            SELECT ids.id_maquina,
                   COALESCE(prod.total, 0) AS total,
                   COALESCE(loss.losses, 0) AS losses
            FROM (
                SELECT @machine_0 AS id_maquina
                {string.Concat(parameters.Skip(1).Select(parameter => $" UNION ALL SELECT {parameter}"))}
            ) ids
            LEFT JOIN (
                SELECT id_maquina, SUM(quantidade) AS total
                FROM eventos_producao
                WHERE id_maquina IN ({string.Join(", ", parameters)})
                  AND ocorrido_em >= @from
                  AND ocorrido_em <= @to
                GROUP BY id_maquina
            ) prod ON prod.id_maquina = ids.id_maquina
            LEFT JOIN (
                SELECT id_maquina, SUM(quantidade) AS losses
                FROM eventos_perda
                WHERE id_maquina IN ({string.Join(", ", parameters)})
                  AND ocorrido_em >= @from
                  AND ocorrido_em <= @to
                GROUP BY id_maquina
            ) loss ON loss.id_maquina = ids.id_maquina
            """;
        for (var index = 0; index < machineIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], machineIds[index]);
        }
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var summaries = new Dictionary<string, RawProductionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries[reader.GetString(0)] = new RawProductionSummary(reader.GetDouble(1), reader.GetDouble(2));
        }

        return summaries;
    }

    private static ProductionSummary BuildProductionSummary(double total, double losses, double target)
    {
        var effectiveLosses = losses > 0 ? losses : total * 0.02;
        var good = Math.Max(total - effectiveLosses, 0);
        var attainment = target > 0 ? total / target * 100 : 0;
        return new ProductionSummary(total, effectiveLosses, good, attainment);
    }

    private static async Task<double> SumQuantityAsync(MySqlConnection connection, string tableName, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(SUM(quantidade), 0)
            FROM {tableName}
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @from
              AND ocorrido_em <= @to
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<StatusSummary> GetStatusSummaryAsync(MySqlConnection connection, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status_maquina,
                   COALESCE(SUM(
                       TIMESTAMPDIFF(
                           MICROSECOND,
                           GREATEST(inicio_em, @from),
                           LEAST(COALESCE(fim_em, @to), @to)
                       ) / 1000000
                   ), 0) AS segundos
            FROM eventos_status_maquina
            WHERE id_maquina = @id_maquina
              AND inicio_em <= @to
              AND COALESCE(fim_em, @to) >= @from
            GROUP BY status_maquina
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var values = new Dictionary<int, double>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetInt32(0)] = reader.GetDouble(1);
        }

        return new StatusSummary(
            values.GetValueOrDefault(0),
            values.GetValueOrDefault(1),
            values.GetValueOrDefault(2),
            values.GetValueOrDefault(3));
    }

    private static async Task<Dictionary<string, StatusSummary>> GetStatusSummariesAsync(
        MySqlConnection connection,
        IReadOnlyList<string> machineIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        if (machineIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        var parameters = machineIds.Select((_, index) => $"@machine_{index}").ToArray();
        command.CommandText = $"""
            SELECT id_maquina, status_maquina,
                   COALESCE(SUM(
                       TIMESTAMPDIFF(
                           MICROSECOND,
                           GREATEST(inicio_em, @from),
                           LEAST(COALESCE(fim_em, @to), @to)
                       ) / 1000000
                   ), 0) AS segundos
            FROM eventos_status_maquina
            WHERE id_maquina IN ({string.Join(", ", parameters)})
              AND inicio_em <= @to
              AND COALESCE(fim_em, @to) >= @from
            GROUP BY id_maquina, status_maquina
            """;
        for (var index = 0; index < machineIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], machineIds[index]);
        }
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var values = machineIds.ToDictionary(id => id, _ => new Dictionary<int, double>());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)][reader.GetInt32(1)] = reader.GetDouble(2);
        }

        return values.ToDictionary(
            item => item.Key,
            item => new StatusSummary(
                item.Value.GetValueOrDefault(0),
                item.Value.GetValueOrDefault(1),
                item.Value.GetValueOrDefault(2),
                item.Value.GetValueOrDefault(3)));
    }

    private async Task<object[]> GetHalfHourProductionTimelineAsync(
        MySqlConnection connection,
        string machineId,
        DateTime localFrom,
        DateTime localTo,
        DateTime utcFrom,
        DateTime utcTo,
        TimeZoneInfo timeZone,
        double? hourlyGoal,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ocorrido_em, quantidade
            FROM eventos_producao
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @from
              AND ocorrido_em <= @to
            ORDER BY ocorrido_em
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@from", utcFrom);
        command.Parameters.AddWithValue("@to", utcTo);

        static DateTime FloorToHalfHour(DateTime value)
        {
            var minute = value.Minute < 30 ? 0 : 30;
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, 0);
        }

        var start = FloorToHalfHour(localFrom);
        var end = FloorToHalfHour(localTo);
        var values = new SortedDictionary<DateTime, double>();
        for (var cursor = start; cursor <= end; cursor = cursor.AddMinutes(30))
        {
            values[cursor] = 0d;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var localOccurredAt = _timeService.UtcToLocal(reader.GetDateTime(0), timeZone);
            var bucket = FloorToHalfHour(localOccurredAt);
            if (!values.ContainsKey(bucket)) continue;

            values[bucket] += reader.GetDouble(1);
        }

        var halfHourGoal = hourlyGoal.HasValue ? hourlyGoal.Value / 2 : (double?)null;
        return values
            .Select(item => new
            {
                hour = item.Key,
                production = item.Value,
                goal = halfHourGoal
            })
            .ToArray();
    }

    private static async Task<ProductionSummary> GetShiftProductionSummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateOnly date,
        long shiftId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT quantidade_produzida, quantidade_perdida, quantidade_boa
            FROM resumos_producao_turno
            WHERE id_maquina = @id_maquina
              AND data_referencia = @data_referencia
              AND id_turno = @id_turno
            LIMIT 1
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@data_referencia", date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@id_turno", shiftId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ProductionSummary(0, 0, 0, 0);
        }

        return new ProductionSummary(
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            0);
    }

    private static async Task<StatusSummary> GetShiftStatusSummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateOnly date,
        long shiftId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tempo_inativa_segundos,
                   tempo_producao_segundos,
                   tempo_ociosa_segundos,
                   tempo_manutencao_segundos
            FROM resumos_producao_turno
            WHERE id_maquina = @id_maquina
              AND data_referencia = @data_referencia
              AND id_turno = @id_turno
            LIMIT 1
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@data_referencia", date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@id_turno", shiftId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new StatusSummary(0, 0, 0, 0);
        }

        return new StatusSummary(
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3));
    }

    private static async Task<List<ShiftDefinition>> GetActiveShiftsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureShiftAccountingColumnAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, codigo, nome, hora_inicio, hora_fim
            FROM turnos
            WHERE ativo = TRUE
              AND contabilizar_producao = TRUE
            ORDER BY hora_inicio, id
            """;

        var shifts = new List<ShiftDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            shifts.Add(new ShiftDefinition(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(4))));
        }

        return shifts;
    }

    private static async Task EnsureShiftAccountingColumnAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'turnos'
              AND COLUMN_NAME = 'contabilizar_producao'
            """;
        if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE turnos ADD COLUMN contabilizar_producao BOOLEAN NOT NULL DEFAULT TRUE AFTER ativo";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ShiftInterval BuildShiftInterval(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var startDateTime = date.ToDateTime(start);
        var endDateTime = date.ToDateTime(end);
        if (endDateTime <= startDateTime)
        {
            endDateTime = endDateTime.AddDays(1);
        }

        return new ShiftInterval(startDateTime, endDateTime);
    }

    private static object EmptyShiftTotals() => new
    {
        production = 0d,
        losses = 0d,
        good = 0d,
        target = 0d,
        attainment_percent = 0d
    };

    private static object EmptyMetrics() => new
    {
        target = 0d,
        total_status_seconds = 0d,
        availability_percent = 0d,
        performance_percent = 0d,
        quality_percent = 0d,
        oee_percent = 0d
    };

    private static double CalculateTarget(MachineGoalSnapshot? goal, DateTime from, DateTime to, string? targetMode = null)
    {
        if (string.Equals(targetMode, "full_day", StringComparison.OrdinalIgnoreCase) &&
            goal?.meta_producao_dia is > 0)
        {
            return goal.meta_producao_dia.Value;
        }

        if (string.Equals(targetMode, "full_day", StringComparison.OrdinalIgnoreCase) &&
            goal?.meta_producao_hora is > 0)
        {
            return goal.meta_producao_hora.Value * 24;
        }

        var hours = Math.Max((to - from).TotalHours, 0);
        if (goal?.meta_producao_hora is > 0)
        {
            return goal.meta_producao_hora.Value * hours;
        }

        if (goal?.meta_producao_dia is > 0)
        {
            return goal.meta_producao_dia.Value * hours / 24;
        }

        return 0;
    }

    private static object BuildMetrics(MachineGoalSnapshot? goal, ProductionSummary production, StatusSummary status, double target)
    {
        var totalStatusSeconds = status.production_seconds +
                                 status.idle_seconds +
                                 status.maintenance_seconds +
                                 status.inactive_seconds;
        var availability = totalStatusSeconds > 0 ? status.production_seconds / totalStatusSeconds * 100 : 0;
        var quality = production.total > 0 ? production.good / production.total * 100 : 0;
        var idealProduction = goal?.tempo_ciclo_ideal_segundos is > 0
            ? status.production_seconds / goal.tempo_ciclo_ideal_segundos.Value
            : 0;
        var performance = idealProduction > 0 ? production.total / idealProduction * 100 : 0;
        var oee = availability > 0 && performance > 0 && quality > 0
            ? availability * performance * quality / 10000
            : 0;

        return new
        {
            target,
            total_status_seconds = totalStatusSeconds,
            availability_percent = availability,
            performance_percent = performance,
            quality_percent = quality,
            oee_percent = oee
        };
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

    private sealed record MachineGoalSnapshot(
        long id,
        double? meta_producao_dia,
        double? meta_producao_hora,
        double? tempo_ciclo_ideal_segundos,
        DateTime vigente_de,
        DateTime? vigente_ate);

    private sealed record ShiftDefinition(long Id, string Code, string Name, TimeOnly Start, TimeOnly End);
    private sealed record ShiftInterval(DateTime Start, DateTime End);
    private sealed record ProductionSummary(double total, double losses, double good, double attainment_percent);
    private sealed record RawProductionSummary(double total, double losses);
    private sealed record StatusSummary(double inactive_seconds, double production_seconds, double idle_seconds, double maintenance_seconds);
    private sealed record ShiftProductionSummary(
        long shift_id,
        string shift_code,
        string shift_name,
        DateTime start,
        DateTime end,
        double target,
        double production,
        double losses,
        double good,
        double attainment_percent,
        double availability_percent,
        double performance_percent,
        double quality_percent,
        double oee_percent);
}
