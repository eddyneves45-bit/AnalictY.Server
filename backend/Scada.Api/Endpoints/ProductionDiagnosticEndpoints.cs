using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Api.Services;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

public static class ProductionDiagnosticEndpoints
{
    public static WebApplication MapProductionDiagnosticEndpoints(this WebApplication app)
    {
        app.MapGet("/api/diagnostics/production", async (
            string? machine_id,
            DateTime? from,
            DateTime? to,
            ScadaDbContext dbContext,
            IReportService reportService,
            ISystemTimeService timeService,
            ITagValueQueue tagQueue,
            IMySqlPersistenceQueue mySqlQueue,
            CancellationToken cancellationToken) =>
        {
            var machines = await dbContext.Machines
                .AsNoTracking()
                .Where(machine => machine.IsActive)
                .OrderBy(machine => machine.Name)
                .Select(machine => new
                {
                    id = machine.Id.ToString(),
                    machine.Name,
                    machine.Code,
                    machine.CostCenter,
                    machine.Location
                })
                .ToListAsync(cancellationToken);

            var selectedMachineId = string.IsNullOrWhiteSpace(machine_id)
                ? machines.FirstOrDefault()?.id
                : machine_id;
            var timeZone = await timeService.GetConfiguredTimeZoneAsync(cancellationToken);
            var endAt = timeService.NormalizeToLocal(to ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone), timeZone);
            var startAt = timeService.NormalizeToLocal(from ?? endAt.Date, timeZone);
            var queryStartAt = timeService.LocalToUtc(startAt, timeZone);
            var queryEndAt = timeService.LocalToUtc(endAt, timeZone);

            var sqlite = selectedMachineId == null
                ? new
                {
                    tags = Array.Empty<object>(),
                    pending_envelopes = Array.Empty<object>(),
                    pending_count = 0,
                    processed_count = 0,
                    failed_count = 0
                }
                : await BuildSqliteSnapshotAsync(dbContext, selectedMachineId, cancellationToken);

            var mysqlConfig = await dbContext.MySqlConfigs
                .AsNoTracking()
                .Where(config => config.IsActive)
                .OrderByDescending(config => config.IsPrimary)
                .ThenByDescending(config => config.IsLocal)
                .ThenBy(config => config.Id)
                .FirstOrDefaultAsync(cancellationToken);

            object mysql;
            if (selectedMachineId == null)
            {
                mysql = new { available = false, message = "Nenhuma máquina ativa encontrada." };
            }
            else if (mysqlConfig == null)
            {
                mysql = new { available = false, message = "Nenhuma conexão MySQL ativa configurada." };
            }
            else
            {
                mysql = await BuildMySqlSnapshotAsync(mysqlConfig, selectedMachineId, queryStartAt, queryEndAt, cancellationToken);
            }

            var matrix = selectedMachineId == null
                ? null
                : await reportService.GetProductionMatrixAsync(new ReportGenerateRequest(
                    "production",
                    selectedMachineId,
                    startAt,
                    endAt,
                    "json"), cancellationToken);
            var statusMatrix = selectedMachineId == null
                ? null
                : await reportService.GetStatusMatrixAsync(new ReportGenerateRequest(
                    "status",
                    selectedMachineId,
                    startAt,
                    endAt,
                    "json"), cancellationToken);

            var mySqlQueueHealth = await mySqlQueue.GetHealthAsync(cancellationToken);

            return Results.Ok(new
            {
                generated_at = DateTime.UtcNow,
                machine_id = selectedMachineId,
                time_zone = new { id = timeZone.Id, display_name = timeZone.DisplayName },
                window = new { from = startAt, to = endAt, query_from_utc = queryStartAt, query_to_utc = queryEndAt },
                machines,
                pipeline = new[]
                {
                    new { step = 1, name = "TAG recebida", table = "TagRuntimeSnapshots", detail = "Último valor por TAG no SQLite" },
                    new { step = 2, name = "Fila MySQL", table = "PendingMySqlEnvelopes", detail = "Envelope aguardando ou já processado" },
                    new { step = 3, name = "Evento MES", table = "eventos_producao / eventos_perda / eventos_status_maquina / eventos_parada", detail = "Registros derivados das TAGs" },
                    new { step = 4, name = "Resumo", table = "resumos_producao_hora / resumos_producao_turno", detail = "Base consolidada para conferência" },
                    new { step = 5, name = "Relatório/KPI", table = "Matriz de produção", detail = "Mesmo cálculo usado no relatório" }
                },
                queues = new
                {
                    tag_queue_depth = tagQueue.ApproximateCount,
                    tag_queue_enqueued = tagQueue.EnqueuedCount,
                    tag_queue_dequeued = tagQueue.DequeuedCount,
                    tag_queue_dropped = tagQueue.DroppedCount,
                    mysql_pending = mySqlQueueHealth.PendingCount,
                    mysql_failed = mySqlQueueHealth.FailedCount
                },
                sqlite,
                mysql,
                report_matrix = matrix,
                status_matrix = statusMatrix
            });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithName("GetProductionDiagnostics");

        return app;
    }

    private static async Task<dynamic> BuildSqliteSnapshotAsync(
        ScadaDbContext dbContext,
        string machineId,
        CancellationToken cancellationToken)
    {
        var mappings = await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map => map.MachineId == machineId && map.IsActive)
            .ToListAsync(cancellationToken);
        var tagIds = mappings.Select(map => map.TagConfigId).ToList();
        var tags = await dbContext.TagConfigs
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id))
            .ToListAsync(cancellationToken);
        var snapshots = await dbContext.TagRuntimeSnapshots
            .AsNoTracking()
            .Where(snapshot => tagIds.Contains(snapshot.TagId))
            .ToDictionaryAsync(snapshot => snapshot.TagId, cancellationToken);

        var mappedTags = mappings
            .Select(map =>
            {
                var tag = tags.FirstOrDefault(item => item.Id == map.TagConfigId);
                snapshots.TryGetValue(map.TagConfigId, out var snapshot);
                return new
                {
                    alias = map.TagAlias,
                    tag_id = map.TagConfigId,
                    name = tag?.TagName,
                    address = tag?.Address,
                    driver = tag?.DriverType,
                    persistence_mode = tag?.PersistenceMode,
                    value = snapshot?.ValueJson,
                    quality = snapshot?.Quality,
                    source_timestamp = snapshot?.SourceTimestamp,
                    last_persisted_at = snapshot?.LastPersistedAt
                };
            })
            .OrderBy(tag => tag.alias)
            .ToList();

        var pending = await dbContext.PendingMySqlEnvelopes
            .AsNoTracking()
            .OrderByDescending(item => item.Id)
            .Take(20)
            .Select(item => new
            {
                item.Id,
                item.Attempts,
                item.NextAttemptAt,
                item.LastError,
                item.CreatedAt,
                item.ProcessedAt
            })
            .ToListAsync(cancellationToken);

        return new
        {
            tags = mappedTags,
            pending_envelopes = pending,
            pending_count = await dbContext.PendingMySqlEnvelopes.CountAsync(item => item.ProcessedAt == null, cancellationToken),
            processed_count = await dbContext.PendingMySqlEnvelopes.CountAsync(item => item.ProcessedAt != null, cancellationToken),
            failed_count = await dbContext.PendingMySqlEnvelopes.CountAsync(item => item.LastError != null, cancellationToken)
        };
    }

    private static async Task<object> BuildMySqlSnapshotAsync(
        MySqlConfig config,
        string machineId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new MySqlConnection(BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);

            var productionEvents = await QueryRowsAsync(connection, """
                SELECT id, id_maquina, id_tag_origem, valor_anterior, valor_atual, quantidade, ocorrido_em
                FROM eventos_producao
                WHERE id_maquina = @machine_id AND ocorrido_em >= @from AND ocorrido_em <= @to
                ORDER BY ocorrido_em DESC
                LIMIT 20
                """, machineId, from, to, cancellationToken);
            var lossEvents = await QueryRowsAsync(connection, """
                SELECT id, id_maquina, id_tag_origem, valor_anterior, valor_atual, quantidade, ocorrido_em
                FROM eventos_perda
                WHERE id_maquina = @machine_id AND ocorrido_em >= @from AND ocorrido_em <= @to
                ORDER BY ocorrido_em DESC
                LIMIT 20
                """, machineId, from, to, cancellationToken);
            var statusEvents = await QueryRowsAsync(connection, """
                SELECT id, id_maquina, status_maquina, descricao_status, inicio_em, fim_em, duracao_segundos, id_tag_origem, qualidade
                FROM eventos_status_maquina
                WHERE id_maquina = @machine_id AND inicio_em <= @to AND COALESCE(fim_em, @to) >= @from
                ORDER BY inicio_em DESC
                LIMIT 20
                """, machineId, from, to, cancellationToken);
            var downtimeEvents = await QueryRowsAsync(connection, """
                SELECT id, id_maquina, inicio_em, fim_em, duracao_segundos, status_origem, motivo_informado
                FROM eventos_parada
                WHERE id_maquina = @machine_id AND inicio_em <= @to AND COALESCE(fim_em, @to) >= @from
                ORDER BY inicio_em DESC
                LIMIT 20
                """, machineId, from, to, cancellationToken);
            var hourlySummary = await QueryRowsAsync(connection, """
                SELECT id_maquina, data_referencia, hora_referencia, quantidade_produzida, quantidade_perdida, quantidade_boa, atualizado_em
                FROM resumos_producao_hora
                WHERE id_maquina = @machine_id AND data_referencia >= DATE(@from) AND data_referencia <= DATE(@to)
                ORDER BY data_referencia DESC, hora_referencia DESC
                LIMIT 24
                """, machineId, from, to, cancellationToken);

            var totals = await QuerySingleAsync(connection, """
                SELECT
                    (SELECT COALESCE(SUM(quantidade), 0) FROM eventos_producao WHERE id_maquina = @machine_id AND ocorrido_em >= @from AND ocorrido_em <= @to) AS produced,
                    (SELECT COALESCE(SUM(quantidade), 0) FROM eventos_perda WHERE id_maquina = @machine_id AND ocorrido_em >= @from AND ocorrido_em <= @to) AS losses,
                    (SELECT COUNT(*) FROM eventos_status_maquina WHERE id_maquina = @machine_id AND inicio_em <= @to AND COALESCE(fim_em, @to) >= @from) AS status_count,
                    (SELECT COUNT(*) FROM eventos_parada WHERE id_maquina = @machine_id AND inicio_em <= @to AND COALESCE(fim_em, @to) >= @from) AS downtime_count
                """, machineId, from, to, cancellationToken);

            var produced = Convert.ToDouble(totals.GetValueOrDefault("produced") ?? 0);
            var losses = Convert.ToDouble(totals.GetValueOrDefault("losses") ?? 0);

            return new
            {
                available = true,
                totals = new
                {
                    produced,
                    losses,
                    good = Math.Max(produced - losses, 0),
                    quality_percent = produced > 0 ? Math.Round((Math.Max(produced - losses, 0) / produced) * 100, 2) : 0,
                    status_events = totals.GetValueOrDefault("status_count") ?? 0,
                    downtime_events = totals.GetValueOrDefault("downtime_count") ?? 0
                },
                production_events = productionEvents,
                loss_events = lossEvents,
                status_events = statusEvents,
                downtime_events = downtimeEvents,
                hourly_summary = hourlySummary
            };
        }
        catch (Exception ex)
        {
            return new { available = false, message = ex.Message };
        }
    }

    private static async Task<List<Dictionary<string, object?>>> QueryRowsAsync(
        MySqlConnection connection,
        string sql,
        string machineId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@machine_id", machineId);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<Dictionary<string, object?>> QuerySingleAsync(
        MySqlConnection connection,
        string sql,
        string machineId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var rows = await QueryRowsAsync(connection, sql, machineId, from, to, cancellationToken);
        return rows.FirstOrDefault() ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
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

}
