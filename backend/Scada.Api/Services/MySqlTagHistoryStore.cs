using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MySqlTagHistoryStore : ITagHistoryStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, string> _lastSerializedValues = new();

    public MySqlTagHistoryStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task PersistIfChangedAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var serializedValue = envelope.Value?.ToString() ?? string.Empty;
        if (_lastSerializedValues.TryGetValue(envelope.TagId, out var lastValue) && lastValue == serializedValue)
        {
            return;
        }

        var config = await GetPrimaryConfigAsync(cancellationToken);
        if (config == null)
        {
            return;
        }

        var machineId = await GetMachineIdAsync(envelope.TagId, cancellationToken);
        await using var connection = CreateConnection(config);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO historico_tags
                (id_tag, nome_tag, id_maquina, valor_texto, qualidade, registrado_em)
            VALUES
                (@id_tag, @nome_tag, @id_maquina, @valor_texto, @qualidade, @registrado_em)
            """;
        AddParameter(command, "@id_tag", envelope.TagId);
        AddParameter(command, "@nome_tag", envelope.TagName);
        AddParameter(command, "@id_maquina", machineId);
        AddParameter(command, "@valor_texto", serializedValue);
        AddParameter(command, "@qualidade", envelope.Quality);
        AddParameter(command, "@registrado_em", envelope.SourceTimestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _lastSerializedValues[envelope.TagId] = serializedValue;
    }

    public async Task<object> QueryAsync(int? tagId, string? tagName, DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryConfigAsync(cancellationToken);
        if (config == null)
        {
            return new { items = Array.Empty<object>(), count = 0, message = "Nenhuma conexão MySQL primária ativa configurada" };
        }

        try
        {
            await using var connection = CreateConnection(config);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = IsSqlServer(connection)
                ? """
                    SELECT TOP (@limit) id, id_tag, nome_tag, id_maquina, valor_texto, qualidade, registrado_em
                    FROM historico_tags
                    WHERE (@tag_id IS NULL OR id_tag = @tag_id)
                      AND (@tag_name IS NULL OR nome_tag = @tag_name)
                      AND (@from IS NULL OR registrado_em >= @from)
                      AND (@to IS NULL OR registrado_em <= @to)
                    ORDER BY registrado_em DESC
                    """
                : """
                    SELECT id, id_tag, nome_tag, id_maquina, valor_texto, qualidade, registrado_em
                    FROM historico_tags
                    WHERE (@tag_id IS NULL OR id_tag = @tag_id)
                      AND (@tag_name IS NULL OR nome_tag = @tag_name)
                      AND (@from IS NULL OR registrado_em >= @from)
                      AND (@to IS NULL OR registrado_em <= @to)
                    ORDER BY registrado_em DESC
                    LIMIT @limit
                    """;
            AddParameter(command, "@tag_id", tagId);
            AddParameter(command, "@tag_name", tagName);
            AddParameter(command, "@from", from);
            AddParameter(command, "@to", to);
            AddParameter(command, "@limit", Math.Clamp(limit, 1, 5000));

            var items = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var idOrdinal = reader.GetOrdinal("id");
            var tagIdOrdinal = reader.GetOrdinal("id_tag");
            var tagNameOrdinal = reader.GetOrdinal("nome_tag");
            var machineIdOrdinal = reader.GetOrdinal("id_maquina");
            var valueTextOrdinal = reader.GetOrdinal("valor_texto");
            var qualityOrdinal = reader.GetOrdinal("qualidade");
            var timestampOrdinal = reader.GetOrdinal("registrado_em");
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new
                {
                    id = reader.GetInt64(idOrdinal),
                    tag_id = reader.IsDBNull(tagIdOrdinal) ? (int?)null : reader.GetInt32(tagIdOrdinal),
                    tag_name = reader.GetString(tagNameOrdinal),
                    machine_id = reader.IsDBNull(machineIdOrdinal) ? null : reader.GetString(machineIdOrdinal),
                    value_text = reader.IsDBNull(valueTextOrdinal) ? null : reader.GetString(valueTextOrdinal),
                    quality = reader.GetString(qualityOrdinal),
                    timestamp = reader.GetDateTime(timestampOrdinal)
                });
            }

            return new { items, count = items.Count };
        }
        catch (MySqlException ex) when (ex.Number == 1146)
        {
            return new { items = Array.Empty<object>(), count = 0, message = "Schema histórico ainda não inicializado" };
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return new { items = Array.Empty<object>(), count = 0, message = "Schema histórico ainda não inicializado" };
        }
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

    private async Task<string?> GetMachineIdAsync(int tagId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        return await dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(mapping => mapping.TagConfigId == tagId && mapping.IsActive)
            .Select(mapping => mapping.MachineId)
            .FirstOrDefaultAsync(cancellationToken);
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
