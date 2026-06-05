using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

public static class DatabaseBrowserEndpoints
{
    public static WebApplication MapDatabaseBrowserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/database-browser")
            .RequireAuthorization(policy => policy.RequireRole("admin"));

        group.MapGet("/connections", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var connections = await dbContext.MySqlConfigs
                .AsNoTracking()
                .OrderByDescending(config => config.IsPrimary)
                .ThenByDescending(config => config.IsActive)
                .ThenBy(config => config.Name)
                .Select(config => new
                {
                    config.Id,
                    config.Name,
                    config.Provider,
                    config.Host,
                    config.Port,
                    config.Database,
                    is_active = config.IsActive,
                    is_primary = config.IsPrimary,
                    is_local = config.IsLocal
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(connections);
        });

        group.MapGet("/connections/{id:int}/databases", async (int id, ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var config = await FindConfigAsync(id, dbContext, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Conexao de banco nao encontrada." });

            var databases = IsSqlServer(config.Provider)
                ? await ListSqlServerDatabasesAsync(config, cancellationToken)
                : await ListMySqlDatabasesAsync(config, cancellationToken);

            return Results.Ok(new { connection_id = id, provider = NormalizeProvider(config.Provider), databases });
        });

        group.MapGet("/connections/{id:int}/tables", async (
            int id,
            string? database,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await FindConfigAsync(id, dbContext, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Conexao de banco nao encontrada." });

            var selectedDatabase = ResolveDatabase(config, database);
            var tables = IsSqlServer(config.Provider)
                ? await ListSqlServerTablesAsync(config, selectedDatabase, cancellationToken)
                : await ListMySqlTablesAsync(config, selectedDatabase, cancellationToken);

            return Results.Ok(new { connection_id = id, provider = NormalizeProvider(config.Provider), database = selectedDatabase, tables });
        });

        group.MapGet("/connections/{id:int}/columns", async (
            int id,
            string? database,
            string? schema,
            string table,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await FindConfigAsync(id, dbContext, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Conexao de banco nao encontrada." });

            var selectedDatabase = ResolveDatabase(config, database);
            var columns = IsSqlServer(config.Provider)
                ? await ListSqlServerColumnsAsync(config, selectedDatabase, schema ?? "dbo", table, cancellationToken)
                : await ListMySqlColumnsAsync(config, selectedDatabase, table, cancellationToken);

            return Results.Ok(new { connection_id = id, provider = NormalizeProvider(config.Provider), database = selectedDatabase, schema, table, columns });
        });

        group.MapGet("/connections/{id:int}/rows", async (
            int id,
            string? database,
            string? schema,
            string table,
            int? limit,
            int? offset,
            string? q,
            string? machine_id,
            string? machine_code,
            string? cost_center,
            DateTime? date_from,
            DateTime? date_to,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await FindConfigAsync(id, dbContext, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Conexao de banco nao encontrada." });

            var selectedDatabase = ResolveDatabase(config, database);
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var skip = Math.Max(offset ?? 0, 0);
            var filters = new BrowserFilters(q, machine_id, machine_code, cost_center, date_from, date_to);
            var result = IsSqlServer(config.Provider)
                ? await ReadSqlServerRowsAsync(config, selectedDatabase, schema ?? "dbo", table, take, skip, filters, cancellationToken)
                : await ReadMySqlRowsAsync(config, selectedDatabase, table, take, skip, filters, cancellationToken);

            return Results.Ok(new
            {
                connection_id = id,
                provider = NormalizeProvider(config.Provider),
                database = selectedDatabase,
                schema,
                table,
                limit = take,
                offset = skip,
                q,
                machine_id,
                machine_code,
                cost_center,
                date_from,
                date_to,
                result.columns,
                result.rows
            });
        });

        group.MapGet("/connections/{id:int}/export.csv", async (
            int id,
            string? database,
            string? schema,
            string table,
            string? q,
            string? machine_id,
            string? machine_code,
            string? cost_center,
            DateTime? date_from,
            DateTime? date_to,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await FindConfigAsync(id, dbContext, cancellationToken);
            if (config == null) return Results.NotFound(new { message = "Conexao de banco nao encontrada." });

            var selectedDatabase = ResolveDatabase(config, database);
            var filters = new BrowserFilters(q, machine_id, machine_code, cost_center, date_from, date_to);
            var result = IsSqlServer(config.Provider)
                ? await ReadSqlServerRowsAsync(config, selectedDatabase, schema ?? "dbo", table, 5000, 0, filters, cancellationToken)
                : await ReadMySqlRowsAsync(config, selectedDatabase, table, 5000, 0, filters, cancellationToken);

            var csv = BuildCsv(result.columns, result.rows);
            var fileName = $"{SanitizeFilePart(selectedDatabase)}_{SanitizeFilePart(table)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
        });

        return app;
    }

    private static Task<MySqlConfig?> FindConfigAsync(int id, ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.MySqlConfigs.AsNoTracking().FirstOrDefaultAsync(config => config.Id == id, cancellationToken);

    private static string ResolveDatabase(MySqlConfig config, string? database) =>
        string.IsNullOrWhiteSpace(database) ? config.Database : database.Trim();

    private static string NormalizeProvider(string? provider)
    {
        if (string.Equals(provider, "SQLServer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "MSSQL", StringComparison.OrdinalIgnoreCase))
        {
            return "SQLServer";
        }

        return "MySQL";
    }

    private static bool IsSqlServer(string? provider) => NormalizeProvider(provider) == "SQLServer";

    private static string BuildMySqlConnectionString(MySqlConfig config, string? database = null)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(config.Host) ? "localhost" : config.Host,
            Port = (uint)(config.Port <= 0 ? 3306 : config.Port),
            UserID = config.User,
            Password = config.Password,
            Database = database ?? config.Database,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = (uint)(config.PoolSize <= 0 ? 10 : config.PoolSize),
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5
        };

        return builder.ConnectionString;
    }

    private static string BuildSqlServerConnectionString(MySqlConfig config, string? database = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = BuildSqlServerDataSource(config),
            InitialCatalog = database ?? config.Database,
            UserID = config.User,
            Password = config.Password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            Pooling = true,
            MaxPoolSize = config.PoolSize <= 0 ? 10 : config.PoolSize
        };

        return builder.ConnectionString;
    }

    private static string BuildSqlServerDataSource(MySqlConfig config)
    {
        var host = string.IsNullOrWhiteSpace(config.Host) ? "localhost" : config.Host.Trim();
        if (host.Contains('\\') || host.Contains(',')) return host;

        var port = config.Port <= 0 ? 1433 : config.Port;
        return $"tcp:{host},{port}";
    }

    private static async Task<IReadOnlyList<object>> ListMySqlDatabasesAsync(MySqlConfig config, CancellationToken cancellationToken)
    {
        var databases = new List<object>();
        await using var connection = new MySqlConnection(BuildMySqlConnectionString(config, null));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SCHEMA_NAME
            FROM information_schema.SCHEMATA
            WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
            ORDER BY SCHEMA_NAME;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new { name = reader.GetString(0), is_default = string.Equals(reader.GetString(0), config.Database, StringComparison.OrdinalIgnoreCase) });
        }

        return databases;
    }

    private static async Task<IReadOnlyList<object>> ListSqlServerDatabasesAsync(MySqlConfig config, CancellationToken cancellationToken)
    {
        var databases = new List<object>();
        await using var connection = new SqlConnection(BuildSqlServerConnectionString(config, "master"));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sys.databases
            WHERE database_id > 4
            ORDER BY name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new { name = reader.GetString(0), is_default = string.Equals(reader.GetString(0), config.Database, StringComparison.OrdinalIgnoreCase) });
        }

        return databases;
    }

    private static async Task<IReadOnlyList<object>> ListMySqlTablesAsync(MySqlConfig config, string database, CancellationToken cancellationToken)
    {
        var tables = new List<object>();
        await using var connection = new MySqlConnection(BuildMySqlConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, TABLE_TYPE, TABLE_ROWS
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @database
            ORDER BY TABLE_NAME;
            """;
        command.Parameters.AddWithValue("@database", database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new
            {
                schema = database,
                name = reader.GetString(0),
                type = reader.GetString(1),
                rows = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2)
            });
        }

        return tables;
    }

    private static async Task<IReadOnlyList<object>> ListSqlServerTablesAsync(MySqlConfig config, string database, CancellationToken cancellationToken)
    {
        var tables = new List<object>();
        await using var connection = new SqlConnection(BuildSqlServerConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name, t.name, 'BASE TABLE' AS table_type, SUM(p.rows) AS rows_count
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new
            {
                schema = reader.GetString(0),
                name = reader.GetString(1),
                type = reader.GetString(2),
                rows = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3)
            });
        }

        return tables;
    }

    private static async Task<IReadOnlyList<object>> ListMySqlColumnsAsync(MySqlConfig config, string database, string table, CancellationToken cancellationToken)
    {
        var columns = new List<object>();
        await using var connection = new MySqlConnection(BuildMySqlConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, ORDINAL_POSITION
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        command.Parameters.AddWithValue("@database", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new
            {
                name = reader.GetString(0),
                data_type = reader.GetString(1),
                full_type = reader.GetString(2),
                nullable = string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                key = reader.GetString(4),
                ordinal = reader.GetInt32(5)
            });
        }

        return columns;
    }

    private static async Task<IReadOnlyList<object>> ListSqlServerColumnsAsync(MySqlConfig config, string database, string schema, string table, CancellationToken cancellationToken)
    {
        var columns = new List<object>();
        await using var connection = new SqlConnection(BuildSqlServerConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, ty.name, c.max_length, c.is_nullable, c.column_id
            FROM sys.columns c
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY c.column_id;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new
            {
                name = reader.GetString(0),
                data_type = reader.GetString(1),
                full_type = $"{reader.GetString(1)}({reader.GetInt16(2)})",
                nullable = reader.GetBoolean(3),
                key = "",
                ordinal = reader.GetInt32(4)
            });
        }

        return columns;
    }

    private static async Task<(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, object?>> rows)> ReadMySqlRowsAsync(
        MySqlConfig config,
        string database,
        string table,
        int limit,
        int offset,
        BrowserFilters filters,
        CancellationToken cancellationToken)
    {
        await EnsureMySqlTableExistsAsync(config, database, table, cancellationToken);
        await using var connection = new MySqlConnection(BuildMySqlConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);

        var tableColumns = await GetMySqlColumnMetadataAsync(connection, database, table, cancellationToken);
        await using var command = connection.CreateCommand();
        var where = BuildMySqlWhere(command, tableColumns, filters);
        var orderBy = BuildMySqlOrderBy(tableColumns);
        command.CommandText = $"SELECT * FROM `{EscapeMySqlIdentifier(table)}`{where}{orderBy} LIMIT @limit OFFSET @offset;";
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        return await ReadRowsAsync(command, cancellationToken);
    }

    private static async Task<(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, object?>> rows)> ReadSqlServerRowsAsync(
        MySqlConfig config,
        string database,
        string schema,
        string table,
        int limit,
        int offset,
        BrowserFilters filters,
        CancellationToken cancellationToken)
    {
        await EnsureSqlServerTableExistsAsync(config, database, schema, table, cancellationToken);
        await using var connection = new SqlConnection(BuildSqlServerConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);

        var tableColumns = await GetSqlServerColumnMetadataAsync(connection, schema, table, cancellationToken);
        await using var command = connection.CreateCommand();
        var where = BuildSqlServerWhere(command, tableColumns, filters);
        var orderBy = BuildSqlServerOrderBy(tableColumns);
        command.CommandText = $"SELECT * FROM [{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(table)}]{where}{orderBy} OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY;";
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        return await ReadRowsAsync(command, cancellationToken);
    }

    private static async Task<(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, object?>> rows)> ReadRowsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : NormalizeCellValue(reader.GetValue(i));
            }
            rows.Add(row);
        }

        return (columns, rows);
    }

    private static object NormalizeCellValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }

    private static async Task EnsureMySqlTableExistsAsync(MySqlConfig config, string database, string table, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildMySqlConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table;
            """;
        command.Parameters.AddWithValue("@database", database);
        command.Parameters.AddWithValue("@table", table);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result <= 0) throw new InvalidOperationException("Tabela nao encontrada.");
    }

    private static async Task EnsureSqlServerTableExistsAsync(MySqlConfig config, string database, string schema, string table, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildSqlServerConnectionString(config, database));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result <= 0) throw new InvalidOperationException("Tabela nao encontrada.");
    }

    private sealed record BrowserFilters(string? Search, string? MachineId, string? MachineCode, string? CostCenter, DateTime? DateFrom, DateTime? DateTo);

    private sealed record BrowserColumn(string Name, string DataType);

    private static async Task<IReadOnlyList<BrowserColumn>> GetMySqlColumnMetadataAsync(MySqlConnection connection, string database, string table, CancellationToken cancellationToken)
    {
        var columns = new List<BrowserColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @database
              AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        command.Parameters.AddWithValue("@database", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(new BrowserColumn(reader.GetString(0), reader.GetString(1)));
        return columns;
    }

    private static async Task<IReadOnlyList<BrowserColumn>> GetSqlServerColumnMetadataAsync(SqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        var columns = new List<BrowserColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, ty.name
            FROM sys.columns c
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema
              AND t.name = @table
            ORDER BY c.column_id;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(new BrowserColumn(reader.GetString(0), reader.GetString(1)));
        return columns;
    }

    private static string BuildMySqlWhere(MySqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters)
    {
        var clauses = new List<string>();
        var textColumns = columns.Where(IsTextColumn).Select(column => column.Name).ToList();

        if (!string.IsNullOrWhiteSpace(filters.Search) && textColumns.Count > 0)
        {
            command.Parameters.AddWithValue("@search", $"%{filters.Search.Trim()}%");
            clauses.Add("(" + string.Join(" OR ", textColumns.Select(column => $"`{EscapeMySqlIdentifier(column)}` LIKE @search")) + ")");
        }

        AddMySqlMachineFilters(command, columns, filters, clauses);
        AddMySqlDateFilters(command, columns, filters, clauses);

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    private static string BuildSqlServerWhere(SqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters)
    {
        var clauses = new List<string>();
        var textColumns = columns.Where(IsTextColumn).Select(column => column.Name).ToList();

        if (!string.IsNullOrWhiteSpace(filters.Search) && textColumns.Count > 0)
        {
            command.Parameters.AddWithValue("@search", $"%{filters.Search.Trim()}%");
            clauses.Add("(" + string.Join(" OR ", textColumns.Select(column => $"[{EscapeSqlServerIdentifier(column)}] LIKE @search")) + ")");
        }

        AddSqlServerMachineFilters(command, columns, filters, clauses);
        AddSqlServerDateFilters(command, columns, filters, clauses);

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    private static bool IsTextColumn(BrowserColumn column)
    {
        var type = column.DataType.ToLowerInvariant();
        return type.Contains("char") || type.Contains("text") || type.Contains("json");
    }

    private static void AddMySqlMachineFilters(MySqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters, List<string> clauses)
    {
        var machineColumn = FindColumn(columns, "id_maquina", "machine_id", "machine", "maquina", "codigo_maquina", "machine_code");
        var machineValues = ResolveMachineFilterValues(machineColumn, filters);
        if (machineValues.Count > 0 && machineColumn != null)
        {
            var parameters = new List<string>();
            for (var i = 0; i < machineValues.Count; i++)
            {
                var parameterName = $"@machine{i}";
                command.Parameters.AddWithValue(parameterName, machineValues[i]);
                parameters.Add(parameterName);
            }

            clauses.Add($"`{EscapeMySqlIdentifier(machineColumn)}` IN ({string.Join(", ", parameters)})");
        }

        var costCenterColumn = FindColumn(columns, "cost_center", "centro_custo", "cc", "centro_de_custo");
        if (!string.IsNullOrWhiteSpace(filters.CostCenter) && costCenterColumn != null)
        {
            command.Parameters.AddWithValue("@costCenter", filters.CostCenter.Trim());
            clauses.Add($"`{EscapeMySqlIdentifier(costCenterColumn)}` = @costCenter");
        }
    }

    private static void AddSqlServerMachineFilters(SqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters, List<string> clauses)
    {
        var machineColumn = FindColumn(columns, "id_maquina", "machine_id", "machine", "maquina", "codigo_maquina", "machine_code");
        var machineValues = ResolveMachineFilterValues(machineColumn, filters);
        if (machineValues.Count > 0 && machineColumn != null)
        {
            var parameters = new List<string>();
            for (var i = 0; i < machineValues.Count; i++)
            {
                var parameterName = $"@machine{i}";
                command.Parameters.AddWithValue(parameterName, machineValues[i]);
                parameters.Add(parameterName);
            }

            clauses.Add($"[{EscapeSqlServerIdentifier(machineColumn)}] IN ({string.Join(", ", parameters)})");
        }

        var costCenterColumn = FindColumn(columns, "cost_center", "centro_custo", "cc", "centro_de_custo");
        if (!string.IsNullOrWhiteSpace(filters.CostCenter) && costCenterColumn != null)
        {
            command.Parameters.AddWithValue("@costCenter", filters.CostCenter.Trim());
            clauses.Add($"[{EscapeSqlServerIdentifier(costCenterColumn)}] = @costCenter");
        }
    }

    private static void AddMySqlDateFilters(MySqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters, List<string> clauses)
    {
        var dateColumn = FindDateColumn(columns);
        if (dateColumn == null) return;

        if (filters.DateFrom.HasValue)
        {
            command.Parameters.AddWithValue("@dateFrom", NormalizeFilterDate(filters.DateFrom.Value));
            clauses.Add($"`{EscapeMySqlIdentifier(dateColumn)}` >= @dateFrom");
        }

        if (filters.DateTo.HasValue)
        {
            command.Parameters.AddWithValue("@dateTo", NormalizeFilterDate(filters.DateTo.Value));
            clauses.Add($"`{EscapeMySqlIdentifier(dateColumn)}` <= @dateTo");
        }
    }

    private static void AddSqlServerDateFilters(SqlCommand command, IReadOnlyList<BrowserColumn> columns, BrowserFilters filters, List<string> clauses)
    {
        var dateColumn = FindDateColumn(columns);
        if (dateColumn == null) return;

        if (filters.DateFrom.HasValue)
        {
            command.Parameters.AddWithValue("@dateFrom", NormalizeFilterDate(filters.DateFrom.Value));
            clauses.Add($"[{EscapeSqlServerIdentifier(dateColumn)}] >= @dateFrom");
        }

        if (filters.DateTo.HasValue)
        {
            command.Parameters.AddWithValue("@dateTo", NormalizeFilterDate(filters.DateTo.Value));
            clauses.Add($"[{EscapeSqlServerIdentifier(dateColumn)}] <= @dateTo");
        }
    }

    private static DateTime NormalizeFilterDate(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Utc => value,
            _ => value
        };
    }

    private static string? FindColumn(IReadOnlyList<BrowserColumn> columns, params string[] names)
    {
        foreach (var expected in names)
        {
            var column = columns.FirstOrDefault(item => string.Equals(item.Name, expected, StringComparison.OrdinalIgnoreCase));
            if (column != null) return column.Name;
        }

        return null;
    }

    private static string? FindDateColumn(IReadOnlyList<BrowserColumn> columns) =>
        FindColumn(columns, "registrado_em", "ocorrido_em", "inicio_em", "fim_em", "criado_em", "atualizado_em", "timestamp", "created_at", "updated_at");

    private static IReadOnlyList<string> ResolveMachineFilterValues(string? machineColumn, BrowserFilters filters)
    {
        if (machineColumn == null) return Array.Empty<string>();

        var values = new List<string>();
        AddIfPresent(values, filters.MachineId);
        AddIfPresent(values, filters.MachineCode);

        return values;
    }

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var normalized = value.Trim();
        if (!values.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(normalized);
        }
    }

    private static string BuildMySqlOrderBy(IReadOnlyList<BrowserColumn> columns)
    {
        var dateColumn = FindDateColumn(columns);
        return dateColumn == null ? " " : $" ORDER BY `{EscapeMySqlIdentifier(dateColumn)}` DESC ";
    }

    private static string BuildSqlServerOrderBy(IReadOnlyList<BrowserColumn> columns)
    {
        var dateColumn = FindDateColumn(columns);
        return dateColumn == null
            ? " ORDER BY (SELECT NULL) "
            : $" ORDER BY [{EscapeSqlServerIdentifier(dateColumn)}] DESC ";
    }

    private static string EscapeMySqlIdentifier(string value) => value.Replace("`", "``");

    private static string EscapeSqlServerIdentifier(string value) => value.Replace("]", "]]");

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "export" : safe;
    }

    private static string BuildCsv(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', columns.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', columns.Select(column => EscapeCsv(row.TryGetValue(column, out var value) ? value : null))));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Contains(';') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }
}
