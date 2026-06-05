using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Scada.Api.Services;

internal class ReportService : IReportService
{
    private const int ProductionStatusValue = 1;
    private const string FtpConfigKey = "FtpExport:Config";
    private static readonly TimeSpan ProductionActivityGracePeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleAfterProductionGracePeriod = TimeSpan.FromMinutes(10);

    private readonly ScadaDbContext _dbContext;
    private readonly ISystemTimeService _timeService;
    private readonly IConfiguration _configuration;

    public ReportService(ScadaDbContext dbContext, ISystemTimeService timeService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _timeService = timeService;
        _configuration = configuration;
    }

    public async Task<object> ListReportsAsync(
        string? machineId,
        string? reportType,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Reports.AsQueryable();

        if (!string.IsNullOrEmpty(machineId))
            query = query.Where(r => r.MachineId == machineId);
        if (!string.IsNullOrEmpty(reportType))
            query = query.Where(r => r.ReportType == reportType);
        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        return new { reports, count = reports.Count };
    }

    public async Task<object> CreateReportAsync(ReportCreateRequest request, CancellationToken cancellationToken = default)
    {
        var report = new Report
        {
            Name = request.name,
            Description = request.description,
            ReportType = request.report_type,
            Schedule = request.schedule,
            Parameters = request.parameters,
            MachineId = request.machine_id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Reports.Add(report);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return report;
    }

    public async Task<ApplicationServiceResult> UpdateReportAsync(int id, ReportUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.Reports.FindAsync(new object[] { id }, cancellationToken);
        if (report == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        if (request.name != null) report.Name = request.name;
        if (request.description != null) report.Description = request.description;
        if (request.schedule != null) report.Schedule = request.schedule;
        if (request.parameters != null) report.Parameters = request.parameters;
        if (request.is_active.HasValue) report.IsActive = request.is_active.Value;
        report.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(report);
    }

    public async Task<ApplicationServiceResult> DeleteReportAsync(int id, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.Reports.FindAsync(new object[] { id }, cancellationToken);
        if (report == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.Reports.Remove(report);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new { message = "Relatório excluído" });
    }

    public async Task<object> GenerateAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { success = false, message = "Nenhuma conexão MySQL ativa configurada" };
        }

        var executionId = await InsertExecutionAsync(config, request, "concluido", "Relatório gerado", cancellationToken);

        return request.report_type switch
        {
            "production" => await BuildProductionSummaryAsync(config, request, executionId, cancellationToken),
            "status" => await BuildStatusSummaryAsync(config, request, executionId, cancellationToken),
            "downtime" => await BuildDowntimeSummaryAsync(config, request, executionId, cancellationToken),
            _ => new { success = false, message = "Tipo de relatório não suportado" }
        };
    }

    public async Task<object> ScheduleAsync(ReportScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { success = false, message = "Nenhuma conexão MySQL ativa configurada" };
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agendamentos_relatorio
                (nome, tipo_relatorio, parametros, formato, periodicidade, horario, destino, proxima_execucao_em)
            VALUES
                (@nome, @tipo_relatorio, @parametros, @formato, @periodicidade, @horario, @destino, @proxima_execucao_em);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@nome", request.nome);
        command.Parameters.AddWithValue("@tipo_relatorio", request.report_type);
        command.Parameters.AddWithValue("@parametros", JsonSerializer.Serialize(new
        {
            request.machine_id,
            request.inicio_em,
            request.fim_em,
            request.incluir_motivos_parada,
            request.dia_semana,
            request.dia_mes,
            request.destino
        }));
        command.Parameters.AddWithValue("@formato", request.formato);
        command.Parameters.AddWithValue("@periodicidade", request.periodicidade);
        command.Parameters.AddWithValue("@horario", request.horario?.ToString("HH:mm:ss"));
        command.Parameters.AddWithValue("@destino", request.destino);
        command.Parameters.AddWithValue("@proxima_execucao_em", ResolveNextRun(request));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        return new
        {
            success = true,
            id,
            message = "Agendamento salvo com sucesso"
        };
    }

    public async Task<object> ListSchedulesAsync(string? machineId, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return Array.Empty<object>();

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nome, tipo_relatorio, parametros, formato, periodicidade, horario, destino,
                   ativo, proxima_execucao_em, ultima_execucao_em, criado_em, atualizado_em
            FROM agendamentos_relatorio
            WHERE (@machine_id IS NULL OR JSON_UNQUOTE(JSON_EXTRACT(parametros, '$.machine_id')) = @machine_id)
            ORDER BY ativo DESC, horario IS NULL, horario, id DESC
            LIMIT 500
            """;
        command.Parameters.AddWithValue("@machine_id", string.IsNullOrWhiteSpace(machineId) ? DBNull.Value : machineId);

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                name = reader.GetString(1),
                report_type = reader.GetString(2),
                parameters = reader.IsDBNull(3) ? "{}" : reader.GetString(3),
                format = reader.GetString(4),
                periodicity = reader.GetString(5),
                time = reader.IsDBNull(6) ? null : reader.GetTimeSpan(6).ToString(@"hh\:mm"),
                destination = reader.IsDBNull(7) ? null : reader.GetString(7),
                active = reader.GetBoolean(8),
                next_run_at = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
                last_run_at = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10),
                created_at = reader.GetDateTime(11),
                updated_at = reader.GetDateTime(12)
            });
        }

        return items;
    }

    public async Task<ApplicationServiceResult> UpdateScheduleAsync(long id, ReportScheduleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Nenhuma conexão MySQL ativa configurada" });
        }

        var nextRun = request.ativo
            ? ResolveNextRun(new ReportScheduleRequest(
                request.nome,
                request.report_type,
                request.machine_id,
                request.inicio_em,
                request.fim_em,
                request.formato,
                request.incluir_motivos_parada,
                request.periodicidade,
                request.horario,
                request.dia_semana,
                request.dia_mes,
                request.destino))
            : null;

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agendamentos_relatorio
            SET nome = @nome,
                tipo_relatorio = @tipo_relatorio,
                parametros = @parametros,
                formato = @formato,
                periodicidade = @periodicidade,
                horario = @horario,
                destino = @destino,
                ativo = @ativo,
                proxima_execucao_em = @proxima_execucao_em,
                atualizado_em = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@nome", request.nome);
        command.Parameters.AddWithValue("@tipo_relatorio", request.report_type);
        command.Parameters.AddWithValue("@parametros", JsonSerializer.Serialize(new
        {
            request.machine_id,
            request.inicio_em,
            request.fim_em,
            request.incluir_motivos_parada,
            request.dia_semana,
            request.dia_mes,
            request.destino
        }));
        command.Parameters.AddWithValue("@formato", request.formato);
        command.Parameters.AddWithValue("@periodicidade", request.periodicidade);
        command.Parameters.AddWithValue("@horario", request.horario?.ToString("HH:mm:ss"));
        command.Parameters.AddWithValue("@destino", request.destino);
        command.Parameters.AddWithValue("@ativo", request.ativo);
        command.Parameters.AddWithValue("@proxima_execucao_em", nextRun);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0) return ApplicationServiceResult.NotFound(new { message = "Agendamento não encontrado." });
        return ApplicationServiceResult.Ok(new { success = true, id, message = "Agendamento atualizado com sucesso." });
    }

    public async Task<string> ExportProductionCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        var matrix = await BuildProductionMatrixAsync(request, null, cancellationToken);
        var visibleShifts = matrix.Shifts
            .Where(shift => !string.Equals(shift.Name, "Normal", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(shift.Code, "normal", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var csv = new StringBuilder();
        csv.Append("Hora");
        foreach (var shift in visibleShifts)
        {
            csv.Append(',').Append(EscapeCsv(shift.Name));
        }
        csv.Append(",Fora de turno\n");
        foreach (var row in matrix.Rows)
        {
            csv.Append(EscapeCsv(row.Hour));
            foreach (var shift in visibleShifts)
            {
                csv.Append(',').Append(FormatCsvNumber(row.Values.TryGetValue(shift.Key, out var value) ? value : 0));
            }
            csv.Append(',').Append(FormatCsvNumber(row.OutsideShift)).AppendLine();
        }
        csv.Append("Total");
        foreach (var shift in visibleShifts)
        {
            csv.Append(',').Append(FormatCsvNumber(matrix.Totals.TryGetValue(shift.Key, out var value) ? value : 0));
        }
        csv.Append(',').Append(FormatCsvNumber(matrix.OutsideShiftTotal)).AppendLine();
        return csv.ToString();
    }

    public async Task<object> GetProductionMatrixAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        return await BuildProductionMatrixAsync(request, null, cancellationToken);
    }

    public async Task<object> GetStatusMatrixAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        return await BuildStatusMatrixAsync(request, null, cancellationToken);
    }

    public async Task<object> GetDowntimeEventsAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        return await BuildDowntimeEventsAsync(request, null, null, cancellationToken);
    }

    public Task<string> ExportCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default)
    {
        return request.report_type switch
        {
            "production" => ExportProductionCsvAsync(request, cancellationToken),
            "status" => ExportStatusCsvAsync(request, cancellationToken),
            "downtime" => ExportDowntimeCsvAsync(request, cancellationToken),
            _ => Task.FromResult(string.Empty)
        };
    }

    public async Task<object> GetMachineDashboardAsync(string machineId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(from, to, timeZone);
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { production_total = 0.0, timeline = Array.Empty<object>(), status_summary = Array.Empty<object>() };
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var productionTotal = await GetProductionTotalAsync(connection, machineId, window.UtcFrom, window.UtcTo, cancellationToken);
        var lossTotal = await GetLossTotalAsync(connection, machineId, window.UtcFrom, window.UtcTo, cancellationToken);
        var lossConfig = await GetLossConfigAsync(machineId, cancellationToken);
        if (lossConfig?.LossSource == "fixed")
        {
            lossTotal = lossConfig.FixedLossValue;
        }
        var goodTotal = Math.Max(productionTotal - lossTotal, 0);
        var qualityPercent = productionTotal > 0 ? (goodTotal / productionTotal) * 100 : 0;
        var timeline = await GetProductionTimelineAsync(connection, machineId, window.UtcFrom, window.UtcTo, timeZone, cancellationToken);
        var statusSummary = await GetStatusSummaryAsync(connection, machineId, window.UtcFrom, window.UtcTo, cancellationToken);

        return new
        {
            production_total = productionTotal,
            loss_total = lossTotal,
            good_total = goodTotal,
            quality_percent = qualityPercent,
            timeline,
            status_summary = statusSummary
        };
    }

    public async Task<object> ListExecutionsAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return Array.Empty<object>();
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, id_agendamento, tipo_relatorio, parametros, formato, caminho_arquivo,
                   status_execucao, mensagem, iniciado_em, finalizado_em
            FROM execucoes_exportacao
            ORDER BY iniciado_em DESC
            LIMIT 100
            """;
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                schedule_id = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                report_type = reader.GetString(2),
                parameters = reader.IsDBNull(3) ? null : reader.GetString(3),
                format = reader.GetString(4),
                file_path = reader.IsDBNull(5) ? null : reader.GetString(5),
                status = reader.GetString(6),
                message = reader.IsDBNull(7) ? null : reader.GetString(7),
                started_at = reader.GetDateTime(8),
                finished_at = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
            });
        }
        return items;
    }

    public async Task<ApplicationServiceResult> DeleteExecutionAsync(long id, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Nenhuma conexão MySQL ativa configurada" });
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM execucoes_exportacao
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id);

        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows == 0)
        {
            return ApplicationServiceResult.NotFound(new { message = "Relatório não encontrado" });
        }

        return ApplicationServiceResult.Ok(new { message = "Relatório excluído" });
    }

    public async Task ExecuteDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return;

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, tipo_relatorio, parametros, formato, periodicidade, horario, destino
            FROM agendamentos_relatorio
            WHERE ativo = TRUE
              AND proxima_execucao_em IS NOT NULL
              AND proxima_execucao_em <= UTC_TIMESTAMP(6)
            ORDER BY proxima_execucao_em
            LIMIT 20
            """;
        var due = new List<(long Id, string Type, string Params, string Format, string Periodicity, TimeSpan? Time, string? Destination)>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? "{}" : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetTimeSpan(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        foreach (var item in due)
        {
            await ExecuteScheduleAsync(config, item, cancellationToken);
        }
    }

    private async Task<object> BuildProductionSummaryAsync(MySqlConfig config, ReportGenerateRequest request, long executionId, CancellationToken cancellationToken)
    {
        var matrix = await BuildProductionMatrixAsync(request, config, cancellationToken);
        return new
        {
            success = true,
            execution_id = executionId,
            report_type = "production",
            matrix,
            count = matrix.Rows.Count
        };
    }

    private static async Task<double> GetProductionTotalAsync(MySqlConnection connection, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(quantidade), 0)
            FROM eventos_producao
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @inicio_em
              AND ocorrido_em <= @fim_em
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@inicio_em", from);
        command.Parameters.AddWithValue("@fim_em", to);
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<double> GetLossTotalAsync(MySqlConnection connection, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(quantidade), 0)
            FROM eventos_perda
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @inicio_em
              AND ocorrido_em <= @fim_em
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@inicio_em", from);
        command.Parameters.AddWithValue("@fim_em", to);
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<double> GetLossTotalForReportAsync(MySqlConfig config, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var lossConfig = await GetLossConfigAsync(machineId, cancellationToken);
        if (lossConfig?.LossSource == "fixed")
        {
            return lossConfig.FixedLossValue;
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        return await GetLossTotalAsync(connection, machineId, from, to, cancellationToken);
    }

    private async Task<MachineOEEConfig?> GetLossConfigAsync(string machineId, CancellationToken cancellationToken)
    {
        return await _dbContext.MachineOEEConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(config => config.MachineId == machineId, cancellationToken);
    }

    private async Task<List<object>> GetProductionTimelineAsync(
        MySqlConnection connection,
        string machineId,
        DateTime from,
        DateTime to,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ocorrido_em, quantidade
            FROM eventos_producao
            WHERE id_maquina = @id_maquina
              AND ocorrido_em >= @inicio_em
              AND ocorrido_em <= @fim_em
            ORDER BY ocorrido_em
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@inicio_em", from);
        command.Parameters.AddWithValue("@fim_em", to);

        static DateTime FloorToHalfHour(DateTime value)
        {
            var minute = value.Minute < 30 ? 0 : 30;
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, 0);
        }

        var localFrom = _timeService.UtcToLocal(from, timeZone);
        var localTo = _timeService.UtcToLocal(to, timeZone);
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
            var local = _timeService.UtcToLocal(reader.GetDateTime(0), timeZone);
            var bucket = FloorToHalfHour(local);
            if (!values.ContainsKey(bucket)) continue;

            values[bucket] = values.GetValueOrDefault(bucket) + reader.GetDouble(1);
        }

        var items = new List<object>();
        var cumulative = 0d;
        foreach (var item in values)
        {
            cumulative += item.Value;
            items.Add(new { time = item.Key, value = cumulative });
        }
        return items;
    }

    private static async Task<List<object>> GetStatusSummaryAsync(MySqlConnection connection, string machineId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status_maquina, descricao_status,
                   SUM(COALESCE(duracao_segundos,
                       TIMESTAMPDIFF(MICROSECOND, inicio_em, @fim_em) / 1000000)) AS segundos
            FROM eventos_status_maquina
            WHERE id_maquina = @id_maquina
              AND inicio_em <= @fim_em
              AND COALESCE(fim_em, @fim_em) >= @inicio_em
            GROUP BY status_maquina, descricao_status
            ORDER BY status_maquina
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId);
        command.Parameters.AddWithValue("@inicio_em", from);
        command.Parameters.AddWithValue("@fim_em", to);

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                status = reader.GetInt32(0),
                description = reader.GetString(1),
                seconds = reader.GetDouble(2)
            });
        }
        return items;
    }

    private async Task<ProductionMatrixResponse> BuildProductionMatrixAsync(
        ReportGenerateRequest request,
        MySqlConfig? config,
        CancellationToken cancellationToken)
    {
        config ??= await GetPrimaryMySqlConfigAsync(cancellationToken);
        var machineLabel = await ResolveMachineLabelAsync(request.machine_id, cancellationToken);
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(request.inicio_em, request.fim_em, timeZone);
        if (config == null)
        {
            return ProductionMatrixResponse.Empty(request with { inicio_em = window.LocalFrom, fim_em = window.LocalTo }, machineLabel);
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var shifts = await LoadReportShiftsAsync(connection, cancellationToken);
        var rows = BuildHourRows(window.LocalFrom, window.LocalTo, shifts);
        var rowByHour = rows.ToDictionary(row => row.HourStart);
        var quantitiesByHour = rows.ToDictionary(
            row => row.HourStart,
            _ => shifts.ToDictionary(shift => shift.Key, _ => 0d));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ocorrido_em, quantidade
            FROM eventos_producao
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND ocorrido_em >= @inicio_em
              AND ocorrido_em <= @fim_em
            ORDER BY ocorrido_em
            """;
        command.Parameters.AddWithValue("@machine_id", request.machine_id);
        command.Parameters.AddWithValue("@inicio_em", window.UtcFrom);
        command.Parameters.AddWithValue("@fim_em", window.UtcTo);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurredAt = _timeService.UtcToLocal(reader.GetDateTime(0), timeZone);
            var quantity = reader.GetDouble(1);
            var hourStart = new DateTime(occurredAt.Year, occurredAt.Month, occurredAt.Day, occurredAt.Hour, 0, 0);
            if (!rowByHour.ContainsKey(hourStart)) continue;

            var shift = FindShift(shifts, window.LocalFrom.Date, occurredAt);
            if (shift == null)
            {
                continue;
            }
            else
            {
                quantitiesByHour[hourStart][shift.Key] += quantity;
            }
        }

        foreach (var row in rows.OrderBy(row => row.HourStart))
        {
            foreach (var shift in shifts)
            {
                row.Values[shift.Key] = quantitiesByHour[row.HourStart][shift.Key];
            }

            row.Total = row.Values.Values.Sum();
        }

        var totals = shifts.ToDictionary(
            shift => shift.Key,
            shift => quantitiesByHour.Values.Sum(values => values[shift.Key]));
        var outsideShiftTotal = 0d;
        var grandTotal = 0d;
        grandTotal = totals.Values.Sum();

        return new ProductionMatrixResponse(
            true,
            "production",
            machineLabel,
            window.LocalFrom,
            window.LocalTo,
            shifts,
            rows,
            totals,
            outsideShiftTotal,
            grandTotal);
    }

    private async Task<string> ResolveMachineLabelAsync(string? machineId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(machineId)) return "Todas as máquinas";
        var machine = await _dbContext.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id.ToString() == machineId, cancellationToken);
        return machine == null ? machineId : $"{machine.Name} ({machine.Code})";
    }

    private static List<ProductionMatrixRow> BuildHourRows(DateTime from, DateTime to, IReadOnlyList<ReportShift> shifts)
    {
        var rows = new List<ProductionMatrixRow>();
        var cursor = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
        var end = new DateTime(to.Year, to.Month, to.Day, to.Hour, 0, 0);
        while (cursor <= end)
        {
            rows.Add(new ProductionMatrixRow(
                cursor,
                cursor.ToString("HH:mm"),
                shifts.ToDictionary(shift => shift.Key, _ => 0d),
                0,
                0));
            cursor = cursor.AddHours(1);
        }
        return rows;
    }

    private static List<StatusMatrixRow> BuildStatusRows(DateTime from, DateTime to)
    {
        var rows = new List<StatusMatrixRow>();
        var cursor = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
        var end = new DateTime(to.Year, to.Month, to.Day, to.Hour, 0, 0);
        while (cursor <= end)
        {
            rows.Add(new StatusMatrixRow(cursor, cursor.ToString("HH:mm"), new StatusValues(0, 0, 0, 0)));
            cursor = cursor.AddHours(1);
        }
        return rows;
    }

    private static void AddStatusMinutes(
        IReadOnlyList<StatusMatrixRow> rows,
        IReadOnlyDictionary<DateTime, StatusMatrixRow> rowByHour,
        int status,
        DateTime start,
        DateTime end)
    {
        if (end <= start) return;
        var cursor = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
        while (cursor < end)
        {
            var nextHour = cursor.AddHours(1);
            var sliceStart = start > cursor ? start : cursor;
            var sliceEnd = end < nextHour ? end : nextHour;
            var minutes = Math.Max((sliceEnd - sliceStart).TotalMinutes, 0);
            if (minutes > 0 && rowByHour.TryGetValue(cursor, out var row))
            {
                var availableMinutes = Math.Max(60d - row.Values.TotalMinutes, 0d);
                var cappedMinutes = Math.Min(minutes, availableMinutes);
                if (cappedMinutes > 0)
                {
                    row.Values = AddMinutes(row.Values, status, cappedMinutes);
                }
            }
            cursor = nextHour;
        }
    }

    private static StatusValues AddMinutes(StatusValues values, int status, double minutes) =>
        status switch
        {
            1 => values with { ProductionMinutes = values.ProductionMinutes + minutes },
            2 => values with { IdleMinutes = values.IdleMinutes + minutes },
            3 => values with { MaintenanceMinutes = values.MaintenanceMinutes + minutes },
            0 => values with { InactiveMinutes = values.InactiveMinutes + minutes },
            _ => values
        };

    private static DateTime MaxDateTime(DateTime value, DateTime minimum) => value > minimum ? value : minimum;
    private static DateTime MinDateTime(DateTime value, DateTime maximum) => value < maximum ? value : maximum;

    private static void AddInactiveAndIdleMinutes(
        IReadOnlyList<StatusMatrixRow> rows,
        IReadOnlyDictionary<DateTime, StatusMatrixRow> rowByHour,
        DateTime start,
        DateTime end,
        DateTime? lastProductionEnd)
    {
        if (end <= start)
        {
            return;
        }

        if (!lastProductionEnd.HasValue)
        {
            AddStatusMinutes(rows, rowByHour, 0, start, end);
            return;
        }

        var idleEnd = lastProductionEnd.Value.Add(IdleAfterProductionGracePeriod);
        if (start < idleEnd)
        {
            var boundedIdleEnd = idleEnd < end ? idleEnd : end;
            AddStatusMinutes(rows, rowByHour, 2, start, boundedIdleEnd);

            if (boundedIdleEnd < end)
            {
                AddStatusMinutes(rows, rowByHour, 0, boundedIdleEnd, end);
            }

            return;
        }

        AddStatusMinutes(rows, rowByHour, 0, start, end);
    }

    private static void AddProductionAwareStatusMinutes(
        IReadOnlyList<StatusMatrixRow> rows,
        IReadOnlyDictionary<DateTime, StatusMatrixRow> rowByHour,
        DateTime start,
        DateTime end,
        IReadOnlyList<DateTime> productionActivityStarts)
    {
        if (end <= start)
        {
            return;
        }

        var cursor = start;
        DateTime? lastProductionEnd = null;
        foreach (var activityStart in productionActivityStarts)
        {
            var activityEnd = activityStart.Add(ProductionActivityGracePeriod);
            if (activityEnd <= cursor)
            {
                continue;
            }

            if (activityStart >= end)
            {
                break;
            }

            var activeStart = activityStart > cursor ? activityStart : cursor;
            var activeEnd = activityEnd < end ? activityEnd : end;
            if (activeStart > cursor)
            {
                AddInactiveAndIdleMinutes(rows, rowByHour, cursor, activeStart, lastProductionEnd);
            }

            AddStatusMinutes(rows, rowByHour, ProductionStatusValue, activeStart, activeEnd);
            lastProductionEnd = activeEnd;
            cursor = activeEnd;
        }

        if (cursor < end)
        {
            AddInactiveAndIdleMinutes(rows, rowByHour, cursor, end, lastProductionEnd);
        }
    }

    private async Task<List<DateTime>> LoadProductionActivityStartsAsync(
        MySqlConnection connection,
        string? machineId,
        DateTime from,
        DateTime to,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ocorrido_em
            FROM eventos_producao
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND ocorrido_em >= @inicio_em
              AND ocorrido_em < @fim_em
              AND quantidade > 0
            ORDER BY ocorrido_em
            """;
        command.Parameters.AddWithValue("@machine_id", machineId);
        command.Parameters.AddWithValue("@inicio_em", from);
        command.Parameters.AddWithValue("@fim_em", to);

        var starts = new List<DateTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            starts.Add(_timeService.UtcToLocal(reader.GetDateTime(0), timeZone));
        }

        return starts;
    }

    private static async Task<List<ReportShift>> LoadReportShiftsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureShiftAccountingColumnAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, codigo, nome, hora_inicio, hora_fim, contabilizar_producao
            FROM turnos
            WHERE ativo = TRUE
              AND contabilizar_producao = TRUE
            ORDER BY hora_inicio, id
            """;
        var shifts = new List<ReportShift>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var start = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3));
            var end = TimeOnly.FromTimeSpan(reader.GetTimeSpan(4));
            shifts.Add(new ReportShift($"shift_{id}", id, code, name, start.ToString("HH:mm"), end.ToString("HH:mm"), start, end));
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

    private static ReportShift? FindShift(IReadOnlyList<ReportShift> shifts, DateTime businessDate, DateTime occurredAt)
    {
        foreach (var shift in shifts)
        {
            var interval = BuildShiftInterval(businessDate, shift);
            if (occurredAt >= interval.Start && occurredAt < interval.End)
            {
                return shift;
            }
        }
        return null;
    }

    private static ShiftInterval BuildShiftInterval(DateTime businessDate, ReportShift shift)
    {
        var start = businessDate.Date.Add(shift.Start.ToTimeSpan());
        var end = businessDate.Date.Add(shift.End.ToTimeSpan());
        if (end <= start)
        {
            end = end.AddDays(1);
        }
        return new ShiftInterval(start, end);
    }

    private async Task<object> BuildStatusSummaryAsync(MySqlConfig config, ReportGenerateRequest request, long executionId, CancellationToken cancellationToken)
    {
        var matrix = await BuildStatusMatrixAsync(request, config, cancellationToken);
        return new
        {
            success = true,
            execution_id = executionId,
            report_type = "status",
            matrix
        };
    }

    private async Task<StatusMatrixResponse> BuildStatusMatrixAsync(
        ReportGenerateRequest request,
        MySqlConfig? config,
        CancellationToken cancellationToken)
    {
        config ??= await GetPrimaryMySqlConfigAsync(cancellationToken);
        var machineLabel = await ResolveMachineLabelAsync(request.machine_id, cancellationToken);
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(request.inicio_em, request.fim_em, timeZone);
        var effectiveUtcTo = MinDateTime(window.UtcTo, DateTime.UtcNow);
        if (config == null)
        {
            return StatusMatrixResponse.Empty(request with { inicio_em = window.LocalFrom, fim_em = window.LocalTo }, machineLabel);
        }

        var rows = BuildStatusRows(window.LocalFrom, window.LocalTo);
        var rowByHour = rows.ToDictionary(row => row.HourStart);
        if (effectiveUtcTo <= window.UtcFrom)
        {
            return new StatusMatrixResponse(
                true,
                "status",
                machineLabel,
                window.LocalFrom,
                window.LocalTo,
                rows,
                new StatusTotals(0, 0, 0, 0, 0));
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status_maquina, inicio_em, COALESCE(fim_em, @fim_em) AS fim_em
            FROM eventos_status_maquina
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND inicio_em < @fim_em
              AND COALESCE(fim_em, @fim_em) > @inicio_em
            ORDER BY inicio_em
            """;
        command.Parameters.AddWithValue("@machine_id", request.machine_id);
        command.Parameters.AddWithValue("@inicio_em", window.UtcFrom);
        command.Parameters.AddWithValue("@fim_em", effectiveUtcTo);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetInt32(0);
            var start = _timeService.UtcToLocal(MaxDateTime(reader.GetDateTime(1), window.UtcFrom), timeZone);
            var end = _timeService.UtcToLocal(MinDateTime(reader.GetDateTime(2), effectiveUtcTo), timeZone);
            AddStatusMinutes(rows, rowByHour, status, start, end);
        }

        var totalsMinutes = new StatusValues(
            rows.Sum(row => row.Values.ProductionMinutes),
            rows.Sum(row => row.Values.IdleMinutes),
            rows.Sum(row => row.Values.MaintenanceMinutes),
            rows.Sum(row => row.Values.InactiveMinutes));

        return new StatusMatrixResponse(
            true,
            "status",
            machineLabel,
            window.LocalFrom,
            window.LocalTo,
            rows,
            new StatusTotals(
                totalsMinutes.ProductionMinutes / 60d,
                totalsMinutes.IdleMinutes / 60d,
                totalsMinutes.MaintenanceMinutes / 60d,
                totalsMinutes.InactiveMinutes / 60d,
                totalsMinutes.TotalMinutes / 60d));
    }

    private async Task<object> BuildDowntimeSummaryAsync(MySqlConfig config, ReportGenerateRequest request, long executionId, CancellationToken cancellationToken)
    {
        return await BuildDowntimeEventsAsync(request, config, executionId, cancellationToken);
    }

    private async Task<DowntimeEventsResponse> BuildDowntimeEventsAsync(
        ReportGenerateRequest request,
        MySqlConfig? config,
        long? executionId,
        CancellationToken cancellationToken)
    {
        config ??= await GetPrimaryMySqlConfigAsync(cancellationToken);
        var machineLabel = await ResolveMachineLabelAsync(request.machine_id, cancellationToken);
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(request.inicio_em, request.fim_em, timeZone);
        var effectiveUtcTo = MinDateTime(window.UtcTo, DateTime.UtcNow);
        if (config == null || effectiveUtcTo <= window.UtcFrom)
        {
            return new DowntimeEventsResponse(
                true,
                executionId,
                "downtime",
                machineLabel,
                window.LocalFrom,
                window.LocalTo,
                Array.Empty<DowntimeEventRow>(),
                0,
                0);
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ep.id_maquina,
                   ep.inicio_em,
                   COALESCE(ep.fim_em, @fim_em) AS fim_em,
                   COALESCE(mp.descricao, ep.motivo_informado, 'Por enquanto(ociosa, manutencao ou inativo)') AS motivo,
                   COALESCE(mp.categoria, 'Nao classificada') AS categoria
            FROM eventos_parada ep
            LEFT JOIN motivos_parada mp ON mp.id = ep.id_motivo_parada
            WHERE (@machine_id IS NULL OR ep.id_maquina = @machine_id)
              AND ep.inicio_em < @fim_em
              AND COALESCE(ep.fim_em, @fim_em) > @inicio_em
            ORDER BY ep.id_maquina, ep.inicio_em
            """;
        command.Parameters.AddWithValue("@machine_id", request.machine_id);
        command.Parameters.AddWithValue("@inicio_em", window.UtcFrom);
        command.Parameters.AddWithValue("@fim_em", effectiveUtcTo);

        var items = new List<DowntimeEventRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var startUtc = MaxDateTime(reader.GetDateTime(1), window.UtcFrom);
            var endUtc = MinDateTime(reader.GetDateTime(2), effectiveUtcTo);
            var startLocal = _timeService.UtcToLocal(startUtc, timeZone);
            var endLocal = _timeService.UtcToLocal(endUtc, timeZone);
            var seconds = Math.Max((endLocal - startLocal).TotalSeconds, 0);
            items.Add(new DowntimeEventRow(
                reader.GetString(0),
                startLocal,
                endLocal,
                reader.GetString(3),
                reader.GetString(4),
                seconds,
                seconds / 60d));
        }

        var totalSeconds = items.Sum(item => item.TotalSeconds);
        return new DowntimeEventsResponse(
            true,
            executionId,
            "downtime",
            machineLabel,
            window.LocalFrom,
            window.LocalTo,
            items,
            items.Count,
            totalSeconds / 60d);
    }

    private async Task<long> InsertExecutionAsync(MySqlConfig config, ReportGenerateRequest request, string status, string message, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO execucoes_exportacao
                (tipo_relatorio, parametros, formato, status_execucao, mensagem, finalizado_em)
            VALUES
                (@tipo_relatorio, @parametros, @formato, @status_execucao, @mensagem, @finalizado_em);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@tipo_relatorio", request.report_type);
        command.Parameters.AddWithValue("@parametros", JsonSerializer.Serialize(request));
        command.Parameters.AddWithValue("@formato", request.formato);
        command.Parameters.AddWithValue("@status_execucao", status);
        command.Parameters.AddWithValue("@mensagem", message);
        command.Parameters.AddWithValue("@finalizado_em", DateTime.UtcNow);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ExecuteScheduleAsync(
        MySqlConfig config,
        (long Id, string Type, string Params, string Format, string Periodicity, TimeSpan? Time, string? Destination) item,
        CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.Deserialize<ScheduledReportParameters>(item.Params) ?? new ScheduledReportParameters();
        var now = DateTime.UtcNow;
        var request = new ReportGenerateRequest(
            item.Type,
            parameters.machine_id,
            parameters.inicio_em ?? now.Date,
            parameters.fim_em ?? now,
            item.Format,
            parameters.incluir_motivos_parada);

        var executionId = await InsertScheduledExecutionAsync(config, item.Id, request, cancellationToken);
        try
        {
            var filePath = IsFtpDestination(item.Destination)
                ? await WriteScheduledFtpAsync(request, executionId, item.Destination!, cancellationToken)
                : await WriteScheduledCsvAsync(request, executionId, cancellationToken);
            await CompleteScheduledExecutionAsync(config, executionId, "concluido", filePath, "Exportação concluída", cancellationToken);
            await UpdateScheduleAfterRunAsync(config, item.Id, item.Periodicity, item.Time, cancellationToken);
        }
        catch (Exception ex)
        {
            await CompleteScheduledExecutionAsync(config, executionId, "falhou", null, ex.Message, cancellationToken);
        }
    }

    private async Task<string> WriteScheduledCsvAsync(ReportGenerateRequest request, long executionId, CancellationToken cancellationToken)
    {
        var dataDirectory = _configuration["AnalictY:DataDirectory"] ?? Directory.GetCurrentDirectory();
        var exportRoot = Path.Combine(dataDirectory, "exports");
        Directory.CreateDirectory(exportRoot);
        var filePath = Path.Combine(exportRoot, $"relatorio-{request.report_type}-{executionId}.csv");
        var csv = request.report_type switch
        {
            _ => await ExportCsvAsync(request, cancellationToken)
        };
        await File.WriteAllTextAsync(filePath, csv, cancellationToken);
        return filePath;
    }

    private async Task<string> WriteScheduledFtpAsync(ReportGenerateRequest request, long executionId, string destination, CancellationToken cancellationToken)
    {
        var ftpConfig = await GetFtpConfigAsync(cancellationToken);
        if (ftpConfig == null || !ftpConfig.enabled)
        {
            throw new InvalidOperationException("Conexão FTP inativa ou não configurada.");
        }

        if (string.Equals(ftpConfig.protocol, "SFTP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Agendamento SFTP real ainda não habilitado. Use FTP/FTPS.");
        }

        var csv = request.report_type switch
        {
            "production" => await ExportProductionCsvAsync(request, cancellationToken),
            _ => await ExportCsvAsync(request, cancellationToken)
        };
        var format = NormalizeScheduledFormat(request.formato);
        var bytes = BuildScheduledReportFile(csv, format, request.report_type);
        var destinationPath = NormalizePath(destination.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase) ? destination[4..] : destination);
        var fileName = $"relatorio-{SanitizeFilePart(request.report_type)}-{executionId}.{format}";

        await EnsureFtpDirectoryAsync(ftpConfig, destinationPath, cancellationToken);
        var uploadRequest = CreateFtpRequest(ftpConfig, fileName, WebRequestMethods.Ftp.UploadFile, destinationPath);
        uploadRequest.ContentLength = bytes.Length;

        using var registration = cancellationToken.Register(() => uploadRequest.Abort());
        await using (var stream = await uploadRequest.GetRequestStreamAsync())
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }

        using var response = (FtpWebResponse)await uploadRequest.GetResponseAsync();
        return $"{destinationPath.TrimEnd('/')}/{fileName}";
    }

    private async Task<long> InsertScheduledExecutionAsync(MySqlConfig config, long scheduleId, ReportGenerateRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO execucoes_exportacao
                (id_agendamento, tipo_relatorio, parametros, formato, status_execucao)
            VALUES
                (@id_agendamento, @tipo_relatorio, @parametros, @formato, 'executando');
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@id_agendamento", scheduleId);
        command.Parameters.AddWithValue("@tipo_relatorio", request.report_type);
        command.Parameters.AddWithValue("@parametros", JsonSerializer.Serialize(request));
        command.Parameters.AddWithValue("@formato", request.formato);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task CompleteScheduledExecutionAsync(MySqlConfig config, long executionId, string status, string? filePath, string message, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE execucoes_exportacao
            SET status_execucao = @status,
                caminho_arquivo = @caminho_arquivo,
                mensagem = @mensagem,
                finalizado_em = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", executionId);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@caminho_arquivo", filePath);
        command.Parameters.AddWithValue("@mensagem", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateScheduleAfterRunAsync(MySqlConfig config, long scheduleId, string periodicity, TimeSpan? time, CancellationToken cancellationToken)
    {
        DateTime? nextRun = periodicity switch
        {
            "daily" => DateTime.UtcNow.Date.AddDays(1).Add(time ?? TimeSpan.Zero),
            "weekly" => DateTime.UtcNow.Date.AddDays(7).Add(time ?? TimeSpan.Zero),
            "monthly" => DateTime.UtcNow.Date.AddMonths(1).Add(time ?? TimeSpan.Zero),
            _ => null
        };
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agendamentos_relatorio
            SET ultima_execucao_em = UTC_TIMESTAMP(6),
                proxima_execucao_em = @proxima_execucao_em,
                ativo = CASE WHEN @proxima_execucao_em IS NULL THEN FALSE ELSE ativo END,
                atualizado_em = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", scheduleId);
        command.Parameters.AddWithValue("@proxima_execucao_em", nextRun);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<MySqlConfig?> GetPrimaryMySqlConfigAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DateTime? ResolveNextRun(ReportScheduleRequest request)
    {
        var now = DateTime.UtcNow;
        var at = request.horario?.ToTimeSpan() ?? TimeSpan.Zero;
        return request.periodicidade switch
        {
            "window" => request.inicio_em,
            "daily" => now.Date.Add(at) > now ? now.Date.Add(at) : now.Date.AddDays(1).Add(at),
            "weekly" => ResolveNextWeekly(now, at, request.dia_semana ?? 1),
            "monthly" => ResolveNextMonthly(now, at, request.dia_mes ?? 1),
            _ => null
        };
    }

    private static bool IsFtpDestination(string? destination) =>
        !string.IsNullOrWhiteSpace(destination) && destination.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase);

    private async Task<ScheduledFtpConfig?> GetFtpConfigAsync(CancellationToken cancellationToken)
    {
        var value = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == FtpConfigKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : JsonSerializer.Deserialize<ScheduledFtpConfig>(value);
    }

    private static async Task EnsureFtpDirectoryAsync(ScheduledFtpConfig config, string destinationPath, CancellationToken cancellationToken)
    {
        var accumulated = "";
        foreach (var segment in NormalizePath(destinationPath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated += "/" + segment;
            try
            {
                var request = CreateFtpRequest(config, null, WebRequestMethods.Ftp.MakeDirectory, accumulated);
                using var registration = cancellationToken.Register(() => request.Abort());
                using var response = (FtpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException ex) when (ex.Response is FtpWebResponse response &&
                                          (response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable ||
                                           response.StatusCode == FtpStatusCode.ActionNotTakenFilenameNotAllowed))
            {
                response.Close();
            }
        }
    }

    private static FtpWebRequest CreateFtpRequest(ScheduledFtpConfig config, string? fileName, string method, string destinationPath)
    {
        var path = NormalizePath(destinationPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            path = $"{path.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}";
        }

        var request = (FtpWebRequest)WebRequest.Create(new Uri($"ftp://{config.host}:{config.port}{path}"));
        request.Method = method;
        request.Credentials = new NetworkCredential(config.username, config.password);
        request.EnableSsl = string.Equals(config.protocol, "FTPS", StringComparison.OrdinalIgnoreCase);
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        return request;
    }

    private static string NormalizePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string NormalizeScheduledFormat(string? format)
    {
        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        return normalized is "csv" or "xml" or "pdf" ? normalized : "csv";
    }

    private static byte[] BuildScheduledReportFile(string csv, string format, string reportType)
    {
        if (format == "xml") return Encoding.UTF8.GetBytes(ConvertScheduledCsvToXml(csv, reportType));
        if (format == "pdf") return BuildScheduledPdf($"Relatorio {reportType}", csv);
        return Encoding.UTF8.GetBytes(csv);
    }

    private static string ConvertScheduledCsvToXml(string csv, string reportType)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headers = lines.Length > 0 ? lines[0].Split(';') : Array.Empty<string>();
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<analicty-report>");
        builder.AppendLine($"  <report-type>{WebUtility.HtmlEncode(reportType)}</report-type>");
        builder.AppendLine("  <rows>");
        for (var i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(';');
            builder.AppendLine("    <row>");
            for (var j = 0; j < values.Length; j++)
            {
                var name = j < headers.Length ? headers[j] : $"coluna_{j + 1}";
                builder.AppendLine($"      <column name=\"{WebUtility.HtmlEncode(name)}\">{WebUtility.HtmlEncode(values[j])}</column>");
            }
            builder.AppendLine("    </row>");
        }
        builder.AppendLine("  </rows>");
        builder.AppendLine("</analicty-report>");
        return builder.ToString();
    }

    private static byte[] BuildScheduledPdf(string title, string csv)
    {
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 10 Tf");
        content.AppendLine("40 800 Td");
        content.AppendLine($"({EscapePdfText(title)}) Tj");
        content.AppendLine("0 -18 Td");
        foreach (var line in csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(48))
        {
            var text = line.Replace(";", " | ");
            if (text.Length > 110) text = text[..110];
            content.AppendLine($"({EscapePdfText(text)}) Tj");
            content.AppendLine("0 -14 Td");
        }
        content.AppendLine("ET");

        var streamBytes = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n{content}endstream\nendobj\n"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(item);
        }
        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.AppendLine("xref");
        pdf.AppendLine($"0 {objects.Count + 1}");
        pdf.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1)) pdf.AppendLine($"{offset:0000000000} 00000 n ");
        pdf.AppendLine("trailer");
        pdf.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        pdf.AppendLine("startxref");
        pdf.AppendLine(xrefOffset.ToString());
        pdf.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string EscapePdfText(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string SanitizeFilePart(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }
        return builder.Length == 0 ? "sem_nome" : builder.ToString();
    }

    private static string BuildConnectionString(MySqlConfig config)
    {
        var builder = new MySqlConnectionStringBuilder
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
        };

        return builder.ConnectionString;
    }

    private async Task<string> ExportStatusCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return "maquina_id,status,descricao,segundos\n";
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_maquina, status_maquina, descricao_status,
                   SUM(COALESCE(duracao_segundos,
                       TIMESTAMPDIFF(MICROSECOND, inicio_em, @fim_em) / 1000000)) AS segundos
            FROM eventos_status_maquina
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND inicio_em <= @fim_em
              AND COALESCE(fim_em, @fim_em) >= @inicio_em
            GROUP BY id_maquina, status_maquina, descricao_status
            ORDER BY id_maquina, status_maquina
            """;
        command.Parameters.AddWithValue("@machine_id", request.machine_id);
        command.Parameters.AddWithValue("@inicio_em", request.inicio_em);
        command.Parameters.AddWithValue("@fim_em", request.fim_em);
        var csv = new StringBuilder("maquina_id,status,descricao,segundos\n");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            csv.AppendLine($"{reader.GetString(0)},{reader.GetInt32(1)},{reader.GetString(2)},{reader.GetDouble(3)}");
        }
        return csv.ToString();
    }

    private async Task<string> ExportDowntimeCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return "maquina_id,trigger,recovery,motivo,total_minutos\n";
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var window = _timeService.BuildWindow(request.inicio_em, request.fim_em, timeZone);
        var effectiveUtcTo = MinDateTime(window.UtcTo, DateTime.UtcNow);
        if (effectiveUtcTo <= window.UtcFrom) return "maquina_id,trigger,recovery,motivo,total_minutos\n";

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ep.id_maquina,
                   ep.inicio_em,
                   COALESCE(ep.fim_em, @fim_em) AS fim_em,
                   COALESCE(mp.descricao, ep.motivo_informado, 'Por enquanto(ociosa, manutencao ou inativo)') AS motivo
            FROM eventos_parada ep
            LEFT JOIN motivos_parada mp ON mp.id = ep.id_motivo_parada
            WHERE (@machine_id IS NULL OR ep.id_maquina = @machine_id)
              AND ep.inicio_em < @fim_em
              AND COALESCE(ep.fim_em, @fim_em) > @inicio_em
            ORDER BY ep.id_maquina, ep.inicio_em
            """;
        command.Parameters.AddWithValue("@machine_id", request.machine_id);
        command.Parameters.AddWithValue("@inicio_em", window.UtcFrom);
        command.Parameters.AddWithValue("@fim_em", effectiveUtcTo);
        var csv = new StringBuilder("maquina_id,trigger,recovery,motivo,total_minutos\n");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var startUtc = MaxDateTime(reader.GetDateTime(1), window.UtcFrom);
            var endUtc = MinDateTime(reader.GetDateTime(2), effectiveUtcTo);
            var startLocal = _timeService.UtcToLocal(startUtc, timeZone);
            var endLocal = _timeService.UtcToLocal(endUtc, timeZone);
            var totalMinutes = Math.Max((endLocal - startLocal).TotalMinutes, 0);
            csv.AppendLine($"{reader.GetString(0)},{startLocal:dd/MM/yyyy HH:mm:ss},{endLocal:dd/MM/yyyy HH:mm:ss},{EscapeCsv(reader.GetString(3))},{FormatCsvNumber(totalMinutes)}");
        }
        return csv.ToString();
    }

    private static DateTime ResolveNextWeekly(DateTime now, TimeSpan time, int targetDay)
    {
        var delta = ((targetDay - (int)now.DayOfWeek) + 7) % 7;
        var candidate = now.Date.AddDays(delta).Add(time);
        return candidate > now ? candidate : candidate.AddDays(7);
    }

    private static DateTime ResolveNextMonthly(DateTime now, TimeSpan time, int day)
    {
        var normalizedDay = Math.Clamp(day, 1, DateTime.DaysInMonth(now.Year, now.Month));
        var candidate = new DateTime(now.Year, now.Month, normalizedDay).Add(time);
        if (candidate > now) return candidate;
        var nextMonth = now.AddMonths(1);
        normalizedDay = Math.Clamp(day, 1, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
        return new DateTime(nextMonth.Year, nextMonth.Month, normalizedDay).Add(time);
    }

    private static string FormatCsvNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private sealed record ReportShift(
        string Key,
        long Id,
        string Code,
        string Name,
        string StartTime,
        string EndTime,
        TimeOnly Start,
        TimeOnly End);

    private sealed record ShiftInterval(DateTime Start, DateTime End);

    private sealed class ProductionMatrixRow
    {
        public ProductionMatrixRow(
            DateTime hourStart,
            string hour,
            Dictionary<string, double> values,
            double outsideShift,
            double total)
        {
            HourStart = hourStart;
            Hour = hour;
            Values = values;
            OutsideShift = outsideShift;
            Total = total;
        }

        public DateTime HourStart { get; }
        public string Hour { get; }
        public Dictionary<string, double> Values { get; }
        public double OutsideShift { get; set; }
        public double Total { get; set; }
    }

    private sealed class StatusMatrixRow
    {
        public StatusMatrixRow(DateTime hourStart, string hour, StatusValues values)
        {
            HourStart = hourStart;
            Hour = hour;
            Values = values;
        }

        public DateTime HourStart { get; }
        public string Hour { get; }
        public StatusValues Values { get; set; }
    }

    private sealed record StatusValues(
        double ProductionMinutes,
        double IdleMinutes,
        double MaintenanceMinutes,
        double InactiveMinutes)
    {
        public double TotalMinutes => ProductionMinutes + IdleMinutes + MaintenanceMinutes + InactiveMinutes;
    }

    private sealed record StatusTotals(
        double ProductionHours,
        double IdleHours,
        double MaintenanceHours,
        double InactiveHours,
        double TotalHours);

    private sealed record DowntimeEventRow(
        string MachineId,
        DateTime TriggerAt,
        DateTime RecoveryAt,
        string Reason,
        string Category,
        double TotalSeconds,
        double TotalMinutes);

    private sealed record DowntimeEventsResponse(
        bool Success,
        long? ExecutionId,
        string ReportType,
        string Machine,
        DateTime StartAt,
        DateTime EndAt,
        IReadOnlyList<DowntimeEventRow> Items,
        int Count,
        double TotalMinutes);

    private sealed record ProductionMatrixResponse(
        bool Success,
        string ReportType,
        string Machine,
        DateTime StartAt,
        DateTime EndAt,
        IReadOnlyList<ReportShift> Shifts,
        IReadOnlyList<ProductionMatrixRow> Rows,
        IReadOnlyDictionary<string, double> Totals,
        double OutsideShiftTotal,
        double GrandTotal)
    {
        public static ProductionMatrixResponse Empty(ReportGenerateRequest request, string machineLabel) =>
            new(
                false,
                "production",
                machineLabel,
                request.inicio_em,
                request.fim_em,
                Array.Empty<ReportShift>(),
                Array.Empty<ProductionMatrixRow>(),
                new Dictionary<string, double>(),
                0,
                0);
    }

    private sealed record StatusMatrixResponse(
        bool Success,
        string ReportType,
        string Machine,
        DateTime StartAt,
        DateTime EndAt,
        IReadOnlyList<StatusMatrixRow> Rows,
        StatusTotals Totals)
    {
        public static StatusMatrixResponse Empty(ReportGenerateRequest request, string machineLabel) =>
            new(
                false,
                "status",
                machineLabel,
                request.inicio_em,
                request.fim_em,
                Array.Empty<StatusMatrixRow>(),
                new StatusTotals(0, 0, 0, 0, 0));
    }

    private sealed record ScheduledReportParameters(
        string? machine_id = null,
        DateTime? inicio_em = null,
        DateTime? fim_em = null,
        bool incluir_motivos_parada = false,
        int? dia_semana = null,
        int? dia_mes = null,
        string? destino = null);

    private sealed class ScheduledFtpConfig
    {
        public bool enabled { get; set; }
        public string protocol { get; set; } = "FTP";
        public string host { get; set; } = "";
        public int port { get; set; } = 21;
        public string username { get; set; } = "";
        public string password { get; set; } = "";
    }
}
