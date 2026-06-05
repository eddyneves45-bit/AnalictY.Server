using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Core.Mes;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MySqlMesEventStore : IMesEventStore
{
    private const int SuspiciousCounterDropConfirmationCount = 3;
    private const int ProductionStatusValue = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, double> _lastProductionValues = new();
    private readonly ConcurrentDictionary<string, double> _lastLossValues = new();
    private readonly ConcurrentDictionary<string, int> _suspiciousProductionDropCounts = new();
    private readonly ConcurrentDictionary<string, int> _suspiciousLossDropCounts = new();
    private readonly ConcurrentDictionary<string, int> _lastStatusValues = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastProductionActivityAt = new();
    private readonly ConcurrentDictionary<string, int> _latestDowntimeReasonCodes = new();

    public MySqlMesEventStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task ProcessAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var mappings = await GetMappingsAsync(envelope.TagId, cancellationToken);
        if (mappings.Count == 0)
        {
            return;
        }

        var config = await GetPrimaryConfigAsync(cancellationToken);
        if (config == null)
        {
            return;
        }

        foreach (var mapping in mappings)
        {
            if (mapping.TagAlias == "production_counter" && TryConvertDouble(envelope.Value, out var productionValue))
            {
                await ProcessProductionAsync(config, mapping.MachineId, envelope, productionValue, cancellationToken);
            }

            if (mapping.TagAlias == "loss_count" && TryConvertDouble(envelope.Value, out var lossValue))
            {
                await ProcessLossAsync(config, mapping.MachineId, envelope, lossValue, cancellationToken);
            }

            if (mapping.TagAlias == "machine_status" && TryConvertInt(envelope.Value, out var statusValue))
            {
                await ProcessStatusAsync(config, mapping.MachineId, envelope, statusValue, cancellationToken);
            }

            if (mapping.TagAlias == "downtime_reason_code" && TryConvertInt(envelope.Value, out var reasonCode))
            {
                await ProcessDowntimeReasonAsync(config, mapping.MachineId, reasonCode, cancellationToken);
            }
        }
    }

    private async Task ProcessProductionAsync(MySqlConfig config, string machineId, TagValueEnvelope envelope, double currentValue, CancellationToken cancellationToken)
    {
        var key = $"{machineId}:{envelope.TagId}";
        if (!_lastProductionValues.TryGetValue(key, out var previousValue))
        {
            _lastProductionValues[key] = currentValue;
            _suspiciousProductionDropCounts.TryRemove(key, out _);
            return;
        }

        var quantity = MesEventRules.CalculateProductionDelta(previousValue, currentValue);
        if (quantity <= 0)
        {
            if (MesEventRules.ShouldAcceptCounterValue(previousValue, currentValue))
            {
                _lastProductionValues[key] = currentValue;
                _suspiciousProductionDropCounts.TryRemove(key, out _);
            }
            else if (ShouldAcceptSuspiciousDrop(_suspiciousProductionDropCounts, key))
            {
                _lastProductionValues[key] = currentValue;
            }

            return;
        }

        await using var connection = CreateConnection(config);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO eventos_producao
                (id_maquina, id_tag_origem, valor_anterior, valor_atual, quantidade, ocorrido_em)
            VALUES
                (@id_maquina, @id_tag_origem, @valor_anterior, @valor_atual, @quantidade, @ocorrido_em)
            """;
        AddParameter(command, "@id_maquina", machineId);
        AddParameter(command, "@id_tag_origem", envelope.TagId);
        AddParameter(command, "@valor_anterior", previousValue);
        AddParameter(command, "@valor_atual", currentValue);
        AddParameter(command, "@quantidade", quantity);
        AddParameter(command, "@ocorrido_em", envelope.SourceTimestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _lastProductionValues[key] = currentValue;
        _lastProductionActivityAt[machineId] = envelope.SourceTimestamp;
        _suspiciousProductionDropCounts.TryRemove(key, out _);

        await using var statusConnection = CreateConnection(config);
        await statusConnection.OpenAsync(cancellationToken);
        await ApplyStatusTransitionAsync(statusConnection, machineId, envelope, ProductionStatusValue, cancellationToken);
    }

    private async Task ProcessLossAsync(MySqlConfig config, string machineId, TagValueEnvelope envelope, double currentValue, CancellationToken cancellationToken)
    {
        var key = $"{machineId}:{envelope.TagId}";
        if (!_lastLossValues.TryGetValue(key, out var previousValue))
        {
            _lastLossValues[key] = currentValue;
            _suspiciousLossDropCounts.TryRemove(key, out _);
            return;
        }

        var quantity = MesEventRules.CalculateProductionDelta(previousValue, currentValue);
        if (quantity <= 0)
        {
            if (MesEventRules.ShouldAcceptCounterValue(previousValue, currentValue))
            {
                _lastLossValues[key] = currentValue;
                _suspiciousLossDropCounts.TryRemove(key, out _);
            }
            else if (ShouldAcceptSuspiciousDrop(_suspiciousLossDropCounts, key))
            {
                _lastLossValues[key] = currentValue;
            }

            return;
        }

        await using var connection = CreateConnection(config);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO eventos_perda
                (id_maquina, id_tag_origem, valor_anterior, valor_atual, quantidade, ocorrido_em)
            VALUES
                (@id_maquina, @id_tag_origem, @valor_anterior, @valor_atual, @quantidade, @ocorrido_em)
            """;
        AddParameter(command, "@id_maquina", machineId);
        AddParameter(command, "@id_tag_origem", envelope.TagId);
        AddParameter(command, "@valor_anterior", previousValue);
        AddParameter(command, "@valor_atual", currentValue);
        AddParameter(command, "@quantidade", quantity);
        AddParameter(command, "@ocorrido_em", envelope.SourceTimestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _lastLossValues[key] = currentValue;
        _suspiciousLossDropCounts.TryRemove(key, out _);
    }

    private static bool ShouldAcceptSuspiciousDrop(ConcurrentDictionary<string, int> counters, string key)
    {
        var count = counters.AddOrUpdate(key, 1, (_, currentCount) => currentCount + 1);
        if (count < SuspiciousCounterDropConfirmationCount)
        {
            return false;
        }

        counters.TryRemove(key, out _);
        return true;
    }

    private async Task ProcessStatusAsync(MySqlConfig config, string machineId, TagValueEnvelope envelope, int statusValue, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(config);
        await connection.OpenAsync(cancellationToken);

        if (await HasNewerStatusEventAsync(connection, machineId, envelope.SourceTimestamp, cancellationToken))
        {
            return;
        }

        await ApplyStatusTransitionAsync(connection, machineId, envelope, NormalizeMachineStatus(statusValue), cancellationToken);
    }

    private async Task ApplyStatusTransitionAsync(DbConnection connection, string machineId, TagValueEnvelope envelope, int statusValue, CancellationToken cancellationToken)
    {
        var key = machineId;
        var previousValue = await ResolvePreviousStatusAsync(connection, key, machineId, cancellationToken);
        if (previousValue == statusValue)
        {
            return;
        }

        await CloseOpenStatusAsync(connection, machineId, envelope.SourceTimestamp, cancellationToken);
        await InsertStatusAsync(connection, machineId, envelope, statusValue, cancellationToken);
        await InsertStatusAlertIfNeededAsync(connection, machineId, statusValue, envelope.SourceTimestamp, cancellationToken);

        if (MesEventRules.ShouldCloseDowntime(statusValue))
        {
            await CloseOpenDowntimeAsync(connection, machineId, envelope.SourceTimestamp, cancellationToken);
        }
        else if (MesEventRules.ShouldOpenDowntime(statusValue))
        {
            if (previousValue.HasValue && MesEventRules.ShouldOpenDowntime(previousValue.Value))
            {
                await CloseOpenDowntimeAsync(connection, machineId, envelope.SourceTimestamp, cancellationToken);
            }

            var reason = await ResolveDowntimeReasonAsync(machineId, cancellationToken);
            await EnsureDowntimeOpenAsync(connection, machineId, envelope.SourceTimestamp, statusValue, reason, cancellationToken);
        }

        _lastStatusValues[key] = statusValue;
    }

    private static int NormalizeMachineStatus(int statusValue) => statusValue is >= 0 and <= 3 ? statusValue : 0;

    private static async Task<bool> HasNewerStatusEventAsync(
        DbConnection connection,
        string machineId,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(inicio_em)
            FROM eventos_status_maquina
            WHERE id_maquina = @id_maquina
            """;
        AddParameter(command, "@id_maquina", machineId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value != null && value != DBNull.Value && Convert.ToDateTime(value) >= timestamp;
    }

    private async Task<int?> ResolvePreviousStatusAsync(DbConnection connection, string key, string machineId, CancellationToken cancellationToken)
    {
        if (_lastStatusValues.TryGetValue(key, out var cachedValue))
        {
            return cachedValue;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = IsSqlServer(connection)
            ? """
                SELECT TOP 1 status_maquina
                FROM eventos_status_maquina
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                ORDER BY inicio_em DESC
                """
            : """
                SELECT status_maquina
                FROM eventos_status_maquina
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                ORDER BY inicio_em DESC
                LIMIT 1
                """;
        AddParameter(command, "@id_maquina", machineId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value)
        {
            return null;
        }

        var statusValue = Convert.ToInt32(value);
        _lastStatusValues[key] = statusValue;
        return statusValue;
    }

    private async Task<DateTime?> ResolveLastProductionActivityAtAsync(DbConnection connection, string machineId, CancellationToken cancellationToken)
    {
        if (_lastProductionActivityAt.TryGetValue(machineId, out var cachedValue))
        {
            return cachedValue;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(ocorrido_em)
            FROM eventos_producao
            WHERE id_maquina = @id_maquina
            """;
        AddParameter(command, "@id_maquina", machineId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value)
        {
            return null;
        }

        var timestamp = Convert.ToDateTime(value);
        _lastProductionActivityAt[machineId] = timestamp;
        return timestamp;
    }

    private static async Task<DateTime?> ResolveOpenStatusStartAsync(DbConnection connection, string machineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsSqlServer(connection)
            ? """
                SELECT TOP 1 inicio_em
                FROM eventos_status_maquina
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                ORDER BY inicio_em DESC
                """
            : """
                SELECT inicio_em
                FROM eventos_status_maquina
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                ORDER BY inicio_em DESC
                LIMIT 1
                """;
        AddParameter(command, "@id_maquina", machineId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    private async Task ProcessDowntimeReasonAsync(MySqlConfig config, string machineId, int reasonCode, CancellationToken cancellationToken)
    {
        _latestDowntimeReasonCodes[machineId] = reasonCode;

        var reason = await ResolveDowntimeReasonAsync(machineId, cancellationToken);
        await using var connection = CreateConnection(config);
        await connection.OpenAsync(cancellationToken);
        await UpdateOpenDowntimeReasonAsync(connection, machineId, reason, cancellationToken);
    }

    private static async Task CloseOpenStatusAsync(DbConnection connection, string machineId, DateTime timestamp, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsSqlServer(connection)
            ? """
                UPDATE eventos_status_maquina
                SET fim_em = @fim_em,
                    duracao_segundos = DATEDIFF_BIG(MILLISECOND, inicio_em, @fim_em) / 1000.0
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                """
            : """
                UPDATE eventos_status_maquina
                SET fim_em = @fim_em,
                    duracao_segundos = TIMESTAMPDIFF(MICROSECOND, inicio_em, @fim_em) / 1000000
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                """;
        AddParameter(command, "@fim_em", timestamp);
        AddParameter(command, "@id_maquina", machineId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStatusAsync(DbConnection connection, string machineId, TagValueEnvelope envelope, int statusValue, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO eventos_status_maquina
                (id_maquina, status_maquina, descricao_status, inicio_em, id_tag_origem, qualidade)
            VALUES
                (@id_maquina, @status_maquina, @descricao_status, @inicio_em, @id_tag_origem, @qualidade)
            """;
        AddParameter(command, "@id_maquina", machineId);
        AddParameter(command, "@status_maquina", statusValue);
        AddParameter(command, "@descricao_status", MesEventRules.DescribeMachineStatus(statusValue));
        AddParameter(command, "@inicio_em", envelope.SourceTimestamp);
        AddParameter(command, "@id_tag_origem", envelope.TagId);
        AddParameter(command, "@qualidade", envelope.Quality);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStatusAlertIfNeededAsync(DbConnection connection, string machineId, int statusValue, DateTime timestamp, CancellationToken cancellationToken)
    {
        var alert = statusValue switch
        {
            0 => new { Type = "machine_inactive", Severity = "warning", Title = "Máquina inativa", Message = "Máquina entrou em estado inativa." },
            3 => new { Type = "machine_maintenance", Severity = "critical", Title = "Máquina em manutenção", Message = "Máquina entrou em estado de manutenção." },
            _ => null
        };

        if (alert == null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alertas
                (tipo_alerta, severidade, titulo, mensagem, id_maquina, criado_em, atualizado_em)
            VALUES
                (@tipo_alerta, @severidade, @titulo, @mensagem, @id_maquina, @criado_em, @atualizado_em)
            """;
        AddParameter(command, "@tipo_alerta", alert.Type);
        AddParameter(command, "@severidade", alert.Severity);
        AddParameter(command, "@titulo", alert.Title);
        AddParameter(command, "@mensagem", alert.Message);
        AddParameter(command, "@id_maquina", machineId);
        AddParameter(command, "@criado_em", timestamp);
        AddParameter(command, "@atualizado_em", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDowntimeOpenAsync(DbConnection connection, string machineId, DateTime timestamp, int statusValue, string? reason, CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = """
            SELECT COUNT(*)
            FROM eventos_parada
            WHERE id_maquina = @id_maquina
              AND fim_em IS NULL
            """;
        AddParameter(exists, "@id_maquina", machineId);
        var openCount = Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken));
        if (openCount > 0)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO eventos_parada
                (id_maquina, inicio_em, status_origem, motivo_informado)
            VALUES
                (@id_maquina, @inicio_em, @status_origem, @motivo_informado)
            """;
        AddParameter(insert, "@id_maquina", machineId);
        AddParameter(insert, "@inicio_em", timestamp);
        AddParameter(insert, "@status_origem", statusValue);
        AddParameter(insert, "@motivo_informado", reason);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateOpenDowntimeReasonAsync(DbConnection connection, string machineId, string? reason, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE eventos_parada
            SET motivo_informado = @motivo_informado
            WHERE id_maquina = @id_maquina
              AND fim_em IS NULL
            """;
        AddParameter(command, "@motivo_informado", reason);
        AddParameter(command, "@id_maquina", machineId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CloseOpenDowntimeAsync(DbConnection connection, string machineId, DateTime timestamp, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsSqlServer(connection)
            ? """
                UPDATE eventos_parada
                SET fim_em = @fim_em,
                    duracao_segundos = DATEDIFF_BIG(MILLISECOND, inicio_em, @fim_em) / 1000.0
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                """
            : """
                UPDATE eventos_parada
                SET fim_em = @fim_em,
                    duracao_segundos = TIMESTAMPDIFF(MICROSECOND, inicio_em, @fim_em) / 1000000
                WHERE id_maquina = @id_maquina
                  AND fim_em IS NULL
                """;
        AddParameter(command, "@fim_em", timestamp);
        AddParameter(command, "@id_maquina", machineId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<MachineTagMap>> GetMappingsAsync(int tagId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        return await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(mapping => mapping.TagConfigId == tagId && mapping.IsActive)
            .ToListAsync(cancellationToken);
    }

    private async Task<MySqlConfig?> GetPrimaryConfigAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        return await dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ResolveDowntimeReasonAsync(string machineId, CancellationToken cancellationToken)
    {
        if (!_latestDowntimeReasonCodes.TryGetValue(machineId, out var code))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var reason = await dbContext.MachineDowntimeReasons
            .AsNoTracking()
            .Where(item => item.MachineId == machineId && item.Code == code && item.IsActive)
            .Select(item => item.Description)
            .FirstOrDefaultAsync(cancellationToken);

        return reason ?? $"Código {code}";
    }

    private static bool TryConvertDouble(object? value, out double result)
    {
        return double.TryParse(Convert.ToString(value), out result);
    }

    private static bool TryConvertInt(object? value, out int result)
    {
        return int.TryParse(Convert.ToString(value), out result);
    }

    private static DbConnection CreateConnection(MySqlConfig config)
    {
        return IsSqlServer(config)
            ? new SqlConnection(BuildSqlServerConnectionString(config))
            : new MySqlConnection(BuildConnectionString(config));
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static bool IsSqlServer(DbConnection connection) => connection is SqlConnection;

    private static bool IsSqlServer(MySqlConfig config) =>
        string.Equals(config.Provider, "SQLServer", StringComparison.OrdinalIgnoreCase);

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

    private static string BuildSqlServerConnectionString(MySqlConfig config)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = BuildSqlServerDataSource(config),
            InitialCatalog = string.IsNullOrWhiteSpace(config.Database) ? "banco_mes_mundial" : config.Database,
            UserID = config.User,
            Password = config.Password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            Pooling = true,
            MaxPoolSize = Math.Max(config.PoolSize, 1)
        };

        return builder.ConnectionString;
    }

    private static string BuildSqlServerDataSource(MySqlConfig config)
    {
        var host = string.IsNullOrWhiteSpace(config.Host) ? "localhost" : config.Host.Trim();
        if (host.Contains('\\') || host.Contains(','))
        {
            return host;
        }

        var port = config.Port <= 0 ? 1433 : config.Port;
        return $"tcp:{host},{port}";
    }
}
