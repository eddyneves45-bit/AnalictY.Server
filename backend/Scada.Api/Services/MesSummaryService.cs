using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MesSummaryService : IMesSummaryService
{
    private readonly ScadaDbContext _dbContext;
    private readonly ISystemTimeService _timeService;

    public MesSummaryService(ScadaDbContext dbContext, ISystemTimeService timeService)
    {
        _dbContext = dbContext;
        _timeService = timeService;
    }

    public async Task RebuildMachineDayAsync(string machineId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return;
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        await RebuildHourlySummaryAsync(connection, machineId, date, timeZone, cancellationToken);
        await RebuildShiftSummaryAsync(connection, machineId, date, timeZone, cancellationToken);
    }

    private async Task<MySqlConfig?> GetPrimaryMySqlConfigAsync(CancellationToken cancellationToken) =>
        await _dbContext.MySqlConfigs.AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task RebuildHourlySummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateOnly date,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue);
        var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var utcStart = _timeService.LocalToUtc(localStart, timeZone);
        var utcEnd = _timeService.LocalToUtc(localEnd, timeZone);
        var productionByHour = await SumQuantityByLocalHourAsync(connection, "eventos_producao", machineId, utcStart, utcEnd, timeZone, cancellationToken);
        var lossesByHour = await SumQuantityByLocalHourAsync(connection, "eventos_perda", machineId, utcStart, utcEnd, timeZone, cancellationToken);

        for (var hour = 0; hour < 24; hour++)
        {
            var production = productionByHour.GetValueOrDefault(hour);
            var losses = lossesByHour.GetValueOrDefault(hour);
            await UpsertHourlySummaryAsync(connection, machineId, date, hour, production, losses, cancellationToken);
        }
    }

    private async Task RebuildShiftSummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateOnly date,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var shifts = await GetActiveShiftsAsync(connection, cancellationToken);
        foreach (var shift in shifts)
        {
            var interval = BuildShiftInterval(date, shift.Start, shift.End);
            var utcStart = _timeService.LocalToUtc(interval.Start, timeZone);
            var utcEnd = _timeService.LocalToUtc(interval.End, timeZone);
            var production = await SumQuantityAsync(connection, "eventos_producao", machineId, utcStart, utcEnd, cancellationToken);
            var losses = await SumQuantityAsync(connection, "eventos_perda", machineId, utcStart, utcEnd, cancellationToken);
            var status = await GetStatusSummaryAsync(connection, machineId, utcStart, utcEnd, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO resumos_producao_turno
                    (id_maquina, data_referencia, id_turno, quantidade_produzida, quantidade_perdida, quantidade_boa,
                     tempo_producao_segundos, tempo_ociosa_segundos, tempo_manutencao_segundos, tempo_inativa_segundos,
                     criado_em, atualizado_em)
                VALUES
                    (@id_maquina, @data_referencia, @id_turno, @quantidade_produzida, @quantidade_perdida, @quantidade_boa,
                     @tempo_producao_segundos, @tempo_ociosa_segundos, @tempo_manutencao_segundos, @tempo_inativa_segundos,
                     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    quantidade_produzida = VALUES(quantidade_produzida),
                    quantidade_perdida = VALUES(quantidade_perdida),
                    quantidade_boa = VALUES(quantidade_boa),
                    tempo_producao_segundos = VALUES(tempo_producao_segundos),
                    tempo_ociosa_segundos = VALUES(tempo_ociosa_segundos),
                    tempo_manutencao_segundos = VALUES(tempo_manutencao_segundos),
                    tempo_inativa_segundos = VALUES(tempo_inativa_segundos),
                    atualizado_em = UTC_TIMESTAMP(6)
                """;
            command.Parameters.AddWithValue("@id_maquina", machineId);
            command.Parameters.AddWithValue("@data_referencia", date.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@id_turno", shift.Id);
            command.Parameters.AddWithValue("@quantidade_produzida", production);
            command.Parameters.AddWithValue("@quantidade_perdida", losses);
            command.Parameters.AddWithValue("@quantidade_boa", Math.Max(production - losses, 0));
            command.Parameters.AddWithValue("@tempo_producao_segundos", status.ProductionSeconds);
            command.Parameters.AddWithValue("@tempo_ociosa_segundos", status.IdleSeconds);
            command.Parameters.AddWithValue("@tempo_manutencao_segundos", status.MaintenanceSeconds);
            command.Parameters.AddWithValue("@tempo_inativa_segundos", status.InactiveSeconds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<List<ShiftDefinition>> GetActiveShiftsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureShiftAccountingColumnAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, hora_inicio, hora_fim
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
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(1)),
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(2))));
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

    private static async Task<double> SumQuantityAsync(
        MySqlConnection connection,
        string tableName,
        string machineId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(SUM(quantidade), 0)
            FROM {tableName}
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @from
              AND ocorrido_em < @to
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<Dictionary<int, double>> SumQuantityByLocalHourAsync(
        MySqlConnection connection,
        string tableName,
        string machineId,
        DateTime utcFrom,
        DateTime utcTo,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT ocorrido_em, quantidade
            FROM {tableName}
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @from
              AND ocorrido_em < @to
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@from", utcFrom);
        command.Parameters.AddWithValue("@to", utcTo);

        var values = new Dictionary<int, double>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var local = _timeService.UtcToLocal(reader.GetDateTime(0), timeZone);
            values[local.Hour] = values.GetValueOrDefault(local.Hour) + reader.GetDouble(1);
        }

        return values;
    }

    private static async Task UpsertHourlySummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateOnly date,
        int hour,
        double production,
        double losses,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO resumos_producao_hora
                (id_maquina, data_referencia, hora_referencia, quantidade_produzida, quantidade_perdida, quantidade_boa, criado_em, atualizado_em)
            VALUES
                (@id_maquina, @data_referencia, @hora_referencia, @quantidade_produzida, @quantidade_perdida, @quantidade_boa, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                quantidade_produzida = VALUES(quantidade_produzida),
                quantidade_perdida = VALUES(quantidade_perdida),
                quantidade_boa = VALUES(quantidade_boa),
                atualizado_em = UTC_TIMESTAMP(6)
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@data_referencia", date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@hora_referencia", hour);
        command.Parameters.AddWithValue("@quantidade_produzida", production);
        command.Parameters.AddWithValue("@quantidade_perdida", losses);
        command.Parameters.AddWithValue("@quantidade_boa", Math.Max(production - losses, 0));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StatusSummary> GetStatusSummaryAsync(
        MySqlConnection connection,
        string machineId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
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
              AND inicio_em < @to
              AND COALESCE(fim_em, @to) > @from
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

    private sealed record ShiftDefinition(long Id, TimeOnly Start, TimeOnly End);
    private sealed record ShiftInterval(DateTime Start, DateTime End);
    private sealed record StatusSummary(double InactiveSeconds, double ProductionSeconds, double IdleSeconds, double MaintenanceSeconds);
}
