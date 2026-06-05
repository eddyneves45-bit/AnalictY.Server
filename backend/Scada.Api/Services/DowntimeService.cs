using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Mes;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class DowntimeService : IDowntimeService
{
    private const string RetentionSettingKey = "DowntimeRetentionDays";
    private const int DefaultRetentionDays = 1;
    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 7;
    private readonly ScadaDbContext _dbContext;

    public DowntimeService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> ListAsync(string? machineId, DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return Array.Empty<object>();

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);
        await CleanupExpiredAsync(connection, retentionDays, cancellationToken);
        var machineLookup = await GetMachineLookupAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT es.id, es.id_maquina, es.inicio_em, es.fim_em, es.duracao_segundos,
                   es.status_maquina, es.descricao_status,
                   ep.id, ep.id_motivo_parada, mp.codigo, mp.descricao, mp.categoria,
                   ep.motivo_informado, ep.observacao, ep.reconhecida_por, ep.reconhecida_em
            FROM eventos_status_maquina es
            LEFT JOIN eventos_parada ep
              ON ep.id_maquina = es.id_maquina
             AND ep.inicio_em = es.inicio_em
             AND ep.status_origem = es.status_maquina
            LEFT JOIN motivos_parada mp ON mp.id = ep.id_motivo_parada
            WHERE (@machine_id IS NULL OR es.id_maquina = @machine_id)
              AND (@from IS NULL OR COALESCE(es.fim_em, UTC_TIMESTAMP(6)) >= @from)
              AND (@to IS NULL OR es.inicio_em <= @to)
            ORDER BY es.inicio_em DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@machine_id", machineId);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var eventMachineId = reader.GetString(1);
            machineLookup.TryGetValue(eventMachineId, out var machine);
            items.Add(new
            {
                id = reader.GetInt64(0),
                machine_id = eventMachineId,
                machine_name = machine?.Name,
                machine_code = machine?.Code,
                start_time = reader.GetDateTime(2),
                end_time = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                duration_seconds = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                status_origin = reader.GetInt32(5),
                status_origin_description = reader.IsDBNull(6) ? MesEventRules.DescribeMachineStatus(reader.GetInt32(5)) : reader.GetString(6),
                downtime_id = reader.IsDBNull(7) ? (long?)null : reader.GetInt64(7),
                reason_id = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8),
                reason_code = reader.IsDBNull(9) ? null : reader.GetString(9),
                reason = reader.IsDBNull(10) ? reader.IsDBNull(12) ? null : reader.GetString(12) : reader.GetString(10),
                category = reader.IsDBNull(11) ? null : reader.GetString(11),
                informed_reason = reader.IsDBNull(12) ? null : reader.GetString(12),
                observation = reader.IsDBNull(13) ? null : reader.GetString(13),
                acknowledged_by = reader.IsDBNull(14) ? null : reader.GetString(14),
                acknowledged_at = reader.IsDBNull(15) ? (DateTime?)null : reader.GetDateTime(15),
                can_classify = !reader.IsDBNull(7)
            });
        }
        return items;
    }

    public async Task<object> ListReasonsAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return Array.Empty<object>();
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, codigo, descricao, categoria
            FROM motivos_parada
            WHERE ativo = TRUE
            ORDER BY categoria, descricao
            """;
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                code = reader.GetString(1),
                description = reader.GetString(2),
                category = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }
        return items;
    }

    public async Task<object> CreateReasonAsync(DowntimeReasonCreateRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return new { success = false, message = "Nenhuma conexão MySQL ativa configurada" };
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO motivos_parada (codigo, descricao, categoria)
            VALUES (@codigo, @descricao, @categoria);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@codigo", request.codigo);
        command.Parameters.AddWithValue("@descricao", request.descricao);
        command.Parameters.AddWithValue("@categoria", request.categoria);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return new { success = true, id };
    }

    public async Task<ApplicationServiceResult> ClassifyAsync(long eventId, DowntimeClassifyRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return ApplicationServiceResult.NotFound("Nenhuma conexão MySQL ativa configurada");
        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE eventos_parada
            SET id_motivo_parada = @reason_id,
                motivo_informado = @motivo_informado,
                observacao = @observacao,
                reconhecida_por = @reconhecida_por,
                reconhecida_em = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", eventId);
        command.Parameters.AddWithValue("@reason_id", request.reason_id);
        command.Parameters.AddWithValue("@motivo_informado", request.motivo_informado);
        command.Parameters.AddWithValue("@observacao", request.observacao);
        command.Parameters.AddWithValue("@reconhecida_por", request.reconhecida_por);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0 ? ApplicationServiceResult.NotFound() : ApplicationServiceResult.Ok(new { message = "Parada classificada" });
    }

    public async Task<object> GetRetentionAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);
        return new
        {
            retention_days = retentionDays,
            min_days = MinRetentionDays,
            max_days = MaxRetentionDays
        };
    }

    public async Task<ApplicationServiceResult> SetRetentionAsync(DowntimeRetentionRequest request, CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Clamp(request.retention_days, MinRetentionDays, MaxRetentionDays);
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == RetentionSettingKey, cancellationToken);
        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = RetentionSettingKey,
                Value = retentionDays.ToString(),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = retentionDays.ToString();
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config != null)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);
            await CleanupExpiredAsync(connection, retentionDays, cancellationToken);
        }

        return ApplicationServiceResult.Ok(new
        {
            retention_days = retentionDays,
            message = "Retenção de paradas salva"
        });
    }

    private async Task<MySqlConfig?> GetPrimaryMySqlConfigAsync(CancellationToken cancellationToken) =>
        await _dbContext.MySqlConfigs.AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var value = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == RetentionSettingKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return int.TryParse(value, out var retentionDays)
            ? Math.Clamp(retentionDays, MinRetentionDays, MaxRetentionDays)
            : DefaultRetentionDays;
    }

    private async Task<Dictionary<string, Machine>> GetMachineLookupAsync(CancellationToken cancellationToken)
    {
        var machines = await _dbContext.Machines.AsNoTracking().ToListAsync(cancellationToken);
        var lookup = new Dictionary<string, Machine>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in machines)
        {
            lookup[machine.Id.ToString()] = machine;
            if (!string.IsNullOrWhiteSpace(machine.Code))
            {
                lookup[machine.Code] = machine;
            }
        }
        return lookup;
    }

    private static async Task CleanupExpiredAsync(MySqlConnection connection, int retentionDays, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM eventos_parada
            WHERE inicio_em < DATE_SUB(CURDATE(), INTERVAL @days_to_subtract DAY)
            """;
        command.Parameters.AddWithValue("@days_to_subtract", Math.Max(retentionDays - 1, 0));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
}
