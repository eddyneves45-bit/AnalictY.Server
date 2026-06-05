using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class MySqlConfigService : IMySqlConfigService
{
    private readonly ScadaDbContext _dbContext;

    public MySqlConfigService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.MySqlConfigs.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> UpsertConfigAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        var validation = ValidateMySqlConfig(normalized);
        if (validation != null)
        {
            return validation;
        }

        var config = normalized.id.HasValue
            ? await _dbContext.MySqlConfigs.FindAsync(new object[] { normalized.id.Value }, cancellationToken)
            : null;

        if (config == null)
        {
            config = new MySqlConfig { CreatedAt = DateTime.UtcNow };
            _dbContext.MySqlConfigs.Add(config);
        }

        config.Name = normalized.name;
        config.Provider = NormalizeProvider(normalized.provider);
        config.Host = normalized.host;
        config.Port = normalized.port;
        config.User = normalized.user;
        config.Password = normalized.password;
        config.Database = normalized.database;
        config.PoolSize = normalized.pool_size;
        config.IsActive = normalized.is_active;
        config.IsPrimary = normalized.is_primary;
        config.IsLocal = normalized.is_local;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(config);
    }

    public async Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MySqlConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.MySqlConfigs.Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Configuração MySQL excluída" });
    }

    public async Task<ApplicationServiceResult> SetPrimaryConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MySqlConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        if (IsSqlServer(config.Provider))
        {
            return ApplicationServiceResult.BadRequest(new
            {
                detail = "SQL Server ainda está em modo segunda opção: conexão, schema e browser. A gravação MES continua usando MySQL até a etapa de persistência SQL Server."
            });
        }

        var allConfigs = await _dbContext.MySqlConfigs.ToListAsync(cancellationToken);
        foreach (var item in allConfigs)
        {
            item.IsPrimary = false;
        }

        config.IsPrimary = true;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Configuração MySQL definida como primária" });
    }

    public async Task<ApplicationServiceResult> SetLocalConfigAsync(int id, bool isLocal, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MySqlConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        config.IsLocal = isLocal;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = isLocal ? "Configuração MySQL definida como local" : "Configuração MySQL definida como remota" });
    }

    public async Task<ApplicationServiceResult> TestConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MySqlConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        try
        {
            if (IsSqlServer(config.Provider))
            {
                await using var sqlServerConnection = new SqlConnection(BuildSqlServerConnectionString(config, includeDatabase: false));
                await sqlServerConnection.OpenAsync(cancellationToken);
                await using var sqlServerCommand = sqlServerConnection.CreateCommand();
                sqlServerCommand.CommandText = "SELECT 1";
                var sqlServerResult = await sqlServerCommand.ExecuteScalarAsync(cancellationToken);

                return ApplicationServiceResult.Ok(new
                {
                    success = true,
                    message = "Conexão SQL Server testada com sucesso",
                    provider = config.Provider,
                    host = config.Host,
                    port = config.Port,
                    database = config.Database,
                    result = sqlServerResult
                });
            }

            await using var connection = new MySqlConnection(BuildConnectionString(config, includeDatabase: true));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return ApplicationServiceResult.Ok(new
            {
                success = true,
                message = "Conexão MySQL testada com sucesso",
                provider = config.Provider,
                host = config.Host,
                port = config.Port,
                database = config.Database,
                result
            });
        }
        catch (Exception ex)
        {
            return ApplicationServiceResult.BadRequest(new
            {
                success = false,
                message = $"Falha ao conectar no banco MES: {ex.Message}",
                provider = config.Provider,
                host = config.Host,
                port = config.Port,
                database = config.Database
            });
        }
    }

    public async Task<ApplicationServiceResult> TestRequestAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        var validation = ValidateMySqlConfig(normalized);
        if (validation != null)
        {
            return validation;
        }

        var config = new MySqlConfig
        {
            Host = normalized.host,
            Port = normalized.port,
            User = normalized.user,
            Password = normalized.password,
            Database = normalized.database,
            PoolSize = normalized.pool_size,
            Provider = NormalizeProvider(normalized.provider)
        };

        try
        {
            if (IsSqlServer(config.Provider))
            {
                await using var sqlServerConnection = new SqlConnection(BuildSqlServerConnectionString(config, includeDatabase: false));
                await sqlServerConnection.OpenAsync(cancellationToken);
                await using var sqlServerCommand = sqlServerConnection.CreateCommand();
                sqlServerCommand.CommandText = "SELECT 1";
                var sqlServerResult = await sqlServerCommand.ExecuteScalarAsync(cancellationToken);

                return ApplicationServiceResult.Ok(new
                {
                    success = true,
                    message = "Conexão SQL Server testada com sucesso",
                    provider = config.Provider,
                    host = config.Host,
                    port = config.Port,
                    database = config.Database,
                    result = sqlServerResult
                });
            }

            await using var connection = new MySqlConnection(BuildConnectionString(config, includeDatabase: true));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return ApplicationServiceResult.Ok(new
            {
                success = true,
                message = "Conexão MySQL testada com sucesso",
                provider = config.Provider,
                host = config.Host,
                port = config.Port,
                database = config.Database,
                result
            });
        }
        catch (Exception ex)
        {
            return ApplicationServiceResult.BadRequest(new
            {
                success = false,
                message = $"Falha ao conectar no banco MES: {ex.Message}",
                provider = config.Provider,
                host = config.Host,
                port = config.Port,
                database = config.Database
            });
        }
    }

    public async Task<ApplicationServiceResult> InitConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MySqlConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        try
        {
            if (IsSqlServer(config.Provider))
            {
                await using (var serverConnection = new SqlConnection(BuildSqlServerConnectionString(config, includeDatabase: false)))
                {
                    await serverConnection.OpenAsync(cancellationToken);
                    await using var createDatabase = serverConnection.CreateCommand();
                    createDatabase.CommandText = $"IF DB_ID(N'{EscapeSqlServerLiteral(config.Database)}') IS NULL CREATE DATABASE [{EscapeSqlServerIdentifier(config.Database)}];";
                    await createDatabase.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var databaseConnection = new SqlConnection(BuildSqlServerConnectionString(config, includeDatabase: true)))
                {
                    await databaseConnection.OpenAsync(cancellationToken);
                    foreach (var statement in GetSqlServerSchemaStatements())
                    {
                        await using var command = databaseConnection.CreateCommand();
                        command.CommandText = statement;
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                return ApplicationServiceResult.Ok(new
                {
                    success = true,
                    message = "Banco de dados SQL Server inicializado com sucesso",
                    provider = config.Provider,
                    database = config.Database
                });
            }

            await using (var serverConnection = new MySqlConnection(BuildConnectionString(config, includeDatabase: false)))
            {
                await serverConnection.OpenAsync(cancellationToken);
                await using var createDatabase = serverConnection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS `{config.Database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                await createDatabase.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var databaseConnection = new MySqlConnection(BuildConnectionString(config, includeDatabase: true)))
            {
                await databaseConnection.OpenAsync(cancellationToken);
                foreach (var statement in GetSchemaStatements())
                {
                    await using var command = databaseConnection.CreateCommand();
                    command.CommandText = statement;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            return ApplicationServiceResult.Ok(new
            {
                success = true,
                message = "Banco de dados MySQL inicializado com sucesso",
                provider = config.Provider,
                database = config.Database
            });
        }
        catch (Exception ex)
        {
            return ApplicationServiceResult.BadRequest(new
            {
                success = false,
                message = $"Falha ao inicializar banco MES: {ex.Message}",
                provider = config.Provider,
                database = config.Database
            });
        }
    }

    private static ApplicationServiceResult? ValidateMySqlConfig(MySqlConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.host))
        {
            return ApplicationServiceResult.BadRequest(new { detail = "Informe o host do banco MES." });
        }
        if (string.IsNullOrWhiteSpace(request.database))
        {
            return ApplicationServiceResult.BadRequest(new { detail = "Informe o banco de dados MES." });
        }
        if (string.IsNullOrWhiteSpace(request.user))
        {
            return ApplicationServiceResult.BadRequest(new { detail = "Informe o usuário do banco MES." });
        }
        if (string.IsNullOrWhiteSpace(request.password))
        {
            return ApplicationServiceResult.BadRequest(new { detail = "Informe a senha do banco MES." });
        }
        if (request.port <= 0 || request.port > 65535)
        {
            return ApplicationServiceResult.BadRequest(new { detail = "Informe uma porta válida." });
        }
        if (request.port == 8000)
        {
            return ApplicationServiceResult.BadRequest(new { detail = "A porta 8000 é da API HTTP. Use a porta real do MySQL, normalmente 3306." });
        }

        return null;
    }

    private static MySqlConfigRequest NormalizeRequest(MySqlConfigRequest request)
    {
        return request with
        {
            name = string.IsNullOrWhiteSpace(request.name) ? "MySQL Local MES" : request.name,
            provider = NormalizeProvider(request.provider),
            host = string.IsNullOrWhiteSpace(request.host) ? "localhost" : request.host,
            port = request.port <= 0 ? (IsSqlServer(request.provider) ? 1433 : 3306) : request.port,
            user = request.user,
            password = request.password,
            database = string.IsNullOrWhiteSpace(request.database) ? "banco_mes_mundial" : request.database,
            pool_size = request.pool_size <= 0 ? 10 : request.pool_size
        };
    }

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

    private static string BuildConnectionString(MySqlConfig config, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(config.Host) ? "localhost" : config.Host,
            Port = (uint)(config.Port <= 0 ? 3306 : config.Port),
            UserID = config.User,
            Password = config.Password,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = (uint)(config.PoolSize <= 0 ? 10 : config.PoolSize),
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5
        };

        if (includeDatabase)
        {
            builder.Database = string.IsNullOrWhiteSpace(config.Database) ? "banco_mes_mundial" : config.Database;
        }

        return builder.ConnectionString;
    }

    private static string BuildSqlServerConnectionString(MySqlConfig config, bool includeDatabase)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = BuildSqlServerDataSource(config),
            UserID = config.User,
            Password = config.Password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            Pooling = true,
            MaxPoolSize = config.PoolSize <= 0 ? 10 : config.PoolSize
        };

        if (includeDatabase)
        {
            builder.InitialCatalog = string.IsNullOrWhiteSpace(config.Database) ? "banco_mes_mundial" : config.Database;
        }
        else
        {
            builder.InitialCatalog = "master";
        }

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

    private static string EscapeSqlServerIdentifier(string value) => value.Replace("]", "]]");

    private static string EscapeSqlServerLiteral(string value) => value.Replace("'", "''");

    private static IReadOnlyList<string> GetSqlServerSchemaStatements()
    {
        return new[]
        {
            """
            IF OBJECT_ID('turnos', 'U') IS NULL
            CREATE TABLE turnos (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                codigo NVARCHAR(32) NOT NULL,
                nome NVARCHAR(128) NOT NULL,
                hora_inicio TIME NOT NULL,
                hora_fim TIME NOT NULL,
                ativo BIT NOT NULL DEFAULT 1,
                contabilizar_producao BIT NOT NULL DEFAULT 1,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT uk_turnos_codigo UNIQUE (codigo)
            );
            """,
            """
            IF OBJECT_ID('historico_tags', 'U') IS NULL
            CREATE TABLE historico_tags (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_tag INT NULL,
                nome_tag NVARCHAR(255) NOT NULL,
                id_maquina NVARCHAR(64) NULL,
                valor_texto NVARCHAR(MAX) NULL,
                qualidade NVARCHAR(64) NOT NULL DEFAULT 'UNKNOWN',
                registrado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_historico_tags_tag_tempo')
                CREATE INDEX idx_historico_tags_tag_tempo ON historico_tags (nome_tag, registrado_em);
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_historico_tags_maquina_tempo')
                CREATE INDEX idx_historico_tags_maquina_tempo ON historico_tags (id_maquina, registrado_em);
            """,
            """
            IF OBJECT_ID('eventos_status_maquina', 'U') IS NULL
            CREATE TABLE eventos_status_maquina (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                status_maquina TINYINT NOT NULL,
                descricao_status NVARCHAR(64) NOT NULL,
                inicio_em DATETIME2(6) NOT NULL,
                fim_em DATETIME2(6) NULL,
                duracao_segundos FLOAT NULL,
                id_tag_origem INT NULL,
                qualidade NVARCHAR(64) NOT NULL DEFAULT 'UNKNOWN'
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_eventos_status_maquina_inicio')
                CREATE INDEX idx_eventos_status_maquina_inicio ON eventos_status_maquina (id_maquina, inicio_em);
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_eventos_status_status_inicio')
                CREATE INDEX idx_eventos_status_status_inicio ON eventos_status_maquina (status_maquina, inicio_em);
            """,
            """
            IF OBJECT_ID('eventos_producao', 'U') IS NULL
            CREATE TABLE eventos_producao (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                id_tag_origem INT NULL,
                valor_anterior FLOAT NULL,
                valor_atual FLOAT NULL,
                quantidade FLOAT NOT NULL DEFAULT 0,
                ocorrido_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_eventos_producao_maquina_tempo')
                CREATE INDEX idx_eventos_producao_maquina_tempo ON eventos_producao (id_maquina, ocorrido_em);
            """,
            """
            IF OBJECT_ID('eventos_perda', 'U') IS NULL
            CREATE TABLE eventos_perda (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                id_tag_origem INT NULL,
                valor_anterior FLOAT NULL,
                valor_atual FLOAT NULL,
                quantidade FLOAT NOT NULL DEFAULT 0,
                ocorrido_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_eventos_perda_maquina_tempo')
                CREATE INDEX idx_eventos_perda_maquina_tempo ON eventos_perda (id_maquina, ocorrido_em);
            """,
            """
            IF OBJECT_ID('motivos_parada', 'U') IS NULL
            CREATE TABLE motivos_parada (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                codigo NVARCHAR(64) NOT NULL,
                descricao NVARCHAR(255) NOT NULL,
                categoria NVARCHAR(128) NULL,
                ativo BIT NOT NULL DEFAULT 1,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT uk_motivos_parada_codigo UNIQUE (codigo)
            );
            """,
            """
            IF OBJECT_ID('eventos_parada', 'U') IS NULL
            CREATE TABLE eventos_parada (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                inicio_em DATETIME2(6) NOT NULL,
                fim_em DATETIME2(6) NULL,
                duracao_segundos FLOAT NULL,
                status_origem TINYINT NULL,
                id_motivo_parada BIGINT NULL,
                motivo_informado NVARCHAR(255) NULL,
                observacao NVARCHAR(MAX) NULL,
                reconhecida_por NVARCHAR(255) NULL,
                reconhecida_em DATETIME2(6) NULL,
                CONSTRAINT fk_eventos_parada_motivo FOREIGN KEY (id_motivo_parada) REFERENCES motivos_parada(id)
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_eventos_parada_maquina_inicio')
                CREATE INDEX idx_eventos_parada_maquina_inicio ON eventos_parada (id_maquina, inicio_em);
            """,
            """
            IF OBJECT_ID('alertas', 'U') IS NULL
            CREATE TABLE alertas (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                tipo_alerta NVARCHAR(64) NOT NULL,
                severidade NVARCHAR(64) NOT NULL,
                titulo NVARCHAR(255) NOT NULL,
                mensagem NVARCHAR(MAX) NOT NULL,
                id_maquina NVARCHAR(64) NULL,
                reconhecido BIT NOT NULL DEFAULT 0,
                reconhecido_por NVARCHAR(255) NULL,
                reconhecido_em DATETIME2(6) NULL,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_alertas_maquina_criado')
                CREATE INDEX idx_alertas_maquina_criado ON alertas (id_maquina, criado_em);
            """,
            """
            IF OBJECT_ID('agendamentos_relatorio', 'U') IS NULL
            CREATE TABLE agendamentos_relatorio (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                nome NVARCHAR(255) NOT NULL,
                tipo_relatorio NVARCHAR(64) NOT NULL,
                parametros NVARCHAR(MAX) NULL,
                formato NVARCHAR(32) NOT NULL DEFAULT 'xlsx',
                periodicidade NVARCHAR(64) NOT NULL,
                horario TIME NULL,
                destino NVARCHAR(255) NULL,
                ativo BIT NOT NULL DEFAULT 1,
                proxima_execucao_em DATETIME2(6) NULL,
                ultima_execucao_em DATETIME2(6) NULL,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF OBJECT_ID('execucoes_exportacao', 'U') IS NULL
            CREATE TABLE execucoes_exportacao (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_agendamento BIGINT NULL,
                tipo_relatorio NVARCHAR(64) NOT NULL,
                parametros NVARCHAR(MAX) NULL,
                formato NVARCHAR(32) NOT NULL,
                caminho_arquivo NVARCHAR(512) NULL,
                status_execucao NVARCHAR(64) NOT NULL,
                mensagem NVARCHAR(MAX) NULL,
                iniciado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                finalizado_em DATETIME2(6) NULL,
                CONSTRAINT fk_execucoes_exportacao_agendamento FOREIGN KEY (id_agendamento) REFERENCES agendamentos_relatorio(id)
            );
            """,
            """
            IF OBJECT_ID('metas_maquina', 'U') IS NULL
            CREATE TABLE metas_maquina (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                meta_producao_dia FLOAT NULL,
                meta_producao_hora FLOAT NULL,
                tempo_ciclo_ideal_segundos FLOAT NULL,
                vigente_de DATETIME2(6) NOT NULL,
                vigente_ate DATETIME2(6) NULL,
                ativo BIT NOT NULL DEFAULT 1,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_metas_maquina_vigencia')
                CREATE INDEX idx_metas_maquina_vigencia ON metas_maquina (id_maquina, vigente_de, vigente_ate);
            """,
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_metas_maquina_ativa')
                CREATE INDEX idx_metas_maquina_ativa ON metas_maquina (id_maquina, ativo);
            """,
            """
            IF OBJECT_ID('resumos_producao_hora', 'U') IS NULL
            CREATE TABLE resumos_producao_hora (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                data_referencia DATE NOT NULL,
                hora_referencia TINYINT NOT NULL,
                quantidade_produzida FLOAT NOT NULL DEFAULT 0,
                quantidade_perdida FLOAT NOT NULL DEFAULT 0,
                quantidade_boa FLOAT NOT NULL DEFAULT 0,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT uk_resumos_producao_hora_maquina_data_hora UNIQUE (id_maquina, data_referencia, hora_referencia)
            );
            """,
            """
            IF OBJECT_ID('resumos_producao_turno', 'U') IS NULL
            CREATE TABLE resumos_producao_turno (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                data_referencia DATE NOT NULL,
                id_turno BIGINT NOT NULL,
                quantidade_produzida FLOAT NOT NULL DEFAULT 0,
                quantidade_perdida FLOAT NOT NULL DEFAULT 0,
                quantidade_boa FLOAT NOT NULL DEFAULT 0,
                tempo_producao_segundos FLOAT NOT NULL DEFAULT 0,
                tempo_ociosa_segundos FLOAT NOT NULL DEFAULT 0,
                tempo_manutencao_segundos FLOAT NOT NULL DEFAULT 0,
                tempo_inativa_segundos FLOAT NOT NULL DEFAULT 0,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                atualizado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT uk_resumos_producao_turno_maquina_data_turno UNIQUE (id_maquina, data_referencia, id_turno),
                CONSTRAINT fk_resumos_producao_turno_turno FOREIGN KEY (id_turno) REFERENCES turnos(id)
            );
            """,
            """
            IF OBJECT_ID('resumos_eficiencia_maquina', 'U') IS NULL
            CREATE TABLE resumos_eficiencia_maquina (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                id_maquina NVARCHAR(64) NOT NULL,
                inicio_periodo DATETIME2(6) NOT NULL,
                fim_periodo DATETIME2(6) NOT NULL,
                disponibilidade_percentual FLOAT NOT NULL DEFAULT 0,
                performance_percentual FLOAT NOT NULL DEFAULT 0,
                qualidade_percentual FLOAT NOT NULL DEFAULT 0,
                oee_percentual FLOAT NOT NULL DEFAULT 0,
                criado_em DATETIME2(6) NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """
        };
    }

    private static IReadOnlyList<string> GetSchemaStatements()
    {
        return new[]
        {
            """
            DROP TABLE IF EXISTS machine_states;
            """,
            """
            DROP TABLE IF EXISTS production_events;
            """,
            """
            DROP TABLE IF EXISTS downtime_events;
            """,
            """
            DROP TABLE IF EXISTS tag_history;
            """,
            """
            DROP TABLE IF EXISTS alerts;
            """,
            """
            CREATE TABLE IF NOT EXISTS turnos (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                codigo VARCHAR(32) NOT NULL,
                nome VARCHAR(128) NOT NULL,
                hora_inicio TIME NOT NULL,
                hora_fim TIME NOT NULL,
                ativo BOOLEAN NOT NULL DEFAULT TRUE,
                contabilizar_producao BOOLEAN NOT NULL DEFAULT TRUE,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uk_turnos_codigo (codigo)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS historico_tags (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_tag INT NULL,
                nome_tag VARCHAR(255) NOT NULL,
                id_maquina VARCHAR(64) NULL,
                valor_texto TEXT NULL,
                qualidade VARCHAR(64) NOT NULL DEFAULT 'UNKNOWN',
                registrado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_historico_tags_tag_tempo (nome_tag, registrado_em),
                INDEX idx_historico_tags_maquina_tempo (id_maquina, registrado_em)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS eventos_status_maquina (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                status_maquina TINYINT NOT NULL,
                descricao_status VARCHAR(64) NOT NULL,
                inicio_em DATETIME(6) NOT NULL,
                fim_em DATETIME(6) NULL,
                duracao_segundos DOUBLE NULL,
                id_tag_origem INT NULL,
                qualidade VARCHAR(64) NOT NULL DEFAULT 'UNKNOWN',
                INDEX idx_eventos_status_maquina_inicio (id_maquina, inicio_em),
                INDEX idx_eventos_status_status_inicio (status_maquina, inicio_em)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS eventos_producao (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                id_tag_origem INT NULL,
                valor_anterior DOUBLE NULL,
                valor_atual DOUBLE NULL,
                quantidade DOUBLE NOT NULL DEFAULT 0,
                ocorrido_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_eventos_producao_maquina_tempo (id_maquina, ocorrido_em)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS eventos_perda (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                id_tag_origem INT NULL,
                valor_anterior DOUBLE NULL,
                valor_atual DOUBLE NULL,
                quantidade DOUBLE NOT NULL DEFAULT 0,
                ocorrido_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_eventos_perda_maquina_tempo (id_maquina, ocorrido_em)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS motivos_parada (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                codigo VARCHAR(64) NOT NULL,
                descricao VARCHAR(255) NOT NULL,
                categoria VARCHAR(128) NULL,
                ativo BOOLEAN NOT NULL DEFAULT TRUE,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uk_motivos_parada_codigo (codigo)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS eventos_parada (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                inicio_em DATETIME(6) NOT NULL,
                fim_em DATETIME(6) NULL,
                duracao_segundos DOUBLE NULL,
                status_origem TINYINT NULL,
                id_motivo_parada BIGINT NULL,
                motivo_informado VARCHAR(255) NULL,
                observacao TEXT NULL,
                reconhecida_por VARCHAR(255) NULL,
                reconhecida_em DATETIME(6) NULL,
                INDEX idx_eventos_parada_maquina_inicio (id_maquina, inicio_em),
                CONSTRAINT fk_eventos_parada_motivo
                    FOREIGN KEY (id_motivo_parada) REFERENCES motivos_parada(id)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS alertas (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                tipo_alerta VARCHAR(64) NOT NULL,
                severidade VARCHAR(64) NOT NULL,
                titulo VARCHAR(255) NOT NULL,
                mensagem TEXT NOT NULL,
                id_maquina VARCHAR(64) NULL,
                reconhecido BOOLEAN NOT NULL DEFAULT FALSE,
                reconhecido_por VARCHAR(255) NULL,
                reconhecido_em DATETIME(6) NULL,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_alertas_maquina_criado (id_maquina, criado_em)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS agendamentos_relatorio (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                nome VARCHAR(255) NOT NULL,
                tipo_relatorio VARCHAR(64) NOT NULL,
                parametros JSON NULL,
                formato VARCHAR(32) NOT NULL DEFAULT 'xlsx',
                periodicidade VARCHAR(64) NOT NULL,
                horario TIME NULL,
                destino VARCHAR(255) NULL,
                ativo BOOLEAN NOT NULL DEFAULT TRUE,
                proxima_execucao_em DATETIME(6) NULL,
                ultima_execucao_em DATETIME(6) NULL,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS execucoes_exportacao (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_agendamento BIGINT NULL,
                tipo_relatorio VARCHAR(64) NOT NULL,
                parametros JSON NULL,
                formato VARCHAR(32) NOT NULL,
                caminho_arquivo VARCHAR(512) NULL,
                status_execucao VARCHAR(64) NOT NULL,
                mensagem TEXT NULL,
                iniciado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                finalizado_em DATETIME(6) NULL,
                CONSTRAINT fk_execucoes_exportacao_agendamento
                    FOREIGN KEY (id_agendamento) REFERENCES agendamentos_relatorio(id)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS metas_maquina (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                meta_producao_dia DOUBLE NULL,
                meta_producao_hora DOUBLE NULL,
                tempo_ciclo_ideal_segundos DOUBLE NULL,
                vigente_de DATETIME(6) NOT NULL,
                vigente_ate DATETIME(6) NULL,
                ativo BOOLEAN NOT NULL DEFAULT TRUE,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_metas_maquina_vigencia (id_maquina, vigente_de, vigente_ate),
                INDEX idx_metas_maquina_ativa (id_maquina, ativo)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS resumos_producao_hora (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                data_referencia DATE NOT NULL,
                hora_referencia TINYINT NOT NULL,
                quantidade_produzida DOUBLE NOT NULL DEFAULT 0,
                quantidade_perdida DOUBLE NOT NULL DEFAULT 0,
                quantidade_boa DOUBLE NOT NULL DEFAULT 0,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uk_resumos_producao_hora_maquina_data_hora (id_maquina, data_referencia, hora_referencia),
                INDEX idx_resumos_producao_hora_data (data_referencia, hora_referencia)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS resumos_producao_turno (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                data_referencia DATE NOT NULL,
                id_turno BIGINT NOT NULL,
                quantidade_produzida DOUBLE NOT NULL DEFAULT 0,
                quantidade_perdida DOUBLE NOT NULL DEFAULT 0,
                quantidade_boa DOUBLE NOT NULL DEFAULT 0,
                tempo_producao_segundos DOUBLE NOT NULL DEFAULT 0,
                tempo_ociosa_segundos DOUBLE NOT NULL DEFAULT 0,
                tempo_manutencao_segundos DOUBLE NOT NULL DEFAULT 0,
                tempo_inativa_segundos DOUBLE NOT NULL DEFAULT 0,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uk_resumos_producao_turno_maquina_data_turno (id_maquina, data_referencia, id_turno),
                INDEX idx_resumos_producao_turno_data (data_referencia, id_turno),
                CONSTRAINT fk_resumos_producao_turno_turno
                    FOREIGN KEY (id_turno) REFERENCES turnos(id)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS resumos_eficiencia_maquina (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                id_maquina VARCHAR(64) NOT NULL,
                inicio_periodo DATETIME(6) NOT NULL,
                fim_periodo DATETIME(6) NOT NULL,
                disponibilidade_percentual DOUBLE NOT NULL DEFAULT 0,
                performance_percentual DOUBLE NOT NULL DEFAULT 0,
                qualidade_percentual DOUBLE NOT NULL DEFAULT 0,
                oee_percentual DOUBLE NOT NULL DEFAULT 0,
                criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_resumos_eficiencia_maquina_periodo (id_maquina, inicio_periodo, fim_periodo)
            );
            """
        };
    }
}
