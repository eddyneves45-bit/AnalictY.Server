using Scada.Core.Models.SQLite;
using DataScadaDbContext = Scada.Data.Models.ScadaDbContext;
using Microsoft.EntityFrameworkCore;

namespace Scada.Api.Data;

internal static class DatabaseSeeder
{
    public static void EnsureCreatedAndSeed(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataScadaDbContext>();
        dbContext.Database.EnsureCreated();
        EnsureTagConfigSchema(dbContext);
        EnsureTagRuntimeSnapshotSchema(dbContext);
        EnsureAlertRuleSchema(dbContext);
        EnsurePendingMySqlEnvelopeSchema(dbContext);
        EnsureMachineDowntimeReasonSchema(dbContext);
        EnsureMachineOeeConfigSchema(dbContext);
        EnsureMachineFolderSchema(dbContext);
        EnsureUserSchema(dbContext);
        EnsureUserSessionSchema(dbContext);
        EnsureAuditLogSchema(dbContext);
        EnsureSystemSettingsSchema(dbContext);
        EnsureTelegramSchema(dbContext);
        EnsureDashboardConfigSchema(dbContext);
        EnsureDatabaseProviderSchema(dbContext);

        SeedMachines(dbContext);
        SeedUsers(dbContext, app.Configuration);
        SeedMySqlConfig(dbContext, app.Configuration);
        BackfillMqttTagConnections(dbContext);
        BackfillOpcuaTagConnections(dbContext);
    }

    private static void EnsureDatabaseProviderSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE MySqlConfigs ADD COLUMN Provider TEXT NOT NULL DEFAULT 'MySQL';
                """;
            try
            {
                command.ExecuteNonQuery();
            }
            catch
            {
                // Column already exists.
            }
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureDashboardConfigSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS DashboardConfigs (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    MachineId TEXT NOT NULL,
                    PeriodPreset TEXT NOT NULL DEFAULT 'today',
                    RefreshInterval TEXT NOT NULL DEFAULT '10',
                    WidgetsJson TEXT NOT NULL,
                    IsDefault INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_DashboardConfigs_MachineId
                    ON DashboardConfigs (MachineId);
                CREATE INDEX IF NOT EXISTS IX_DashboardConfigs_IsActive
                    ON DashboardConfigs (IsActive);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureTelegramSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS TelegramConnections (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    BotToken TEXT NOT NULL,
                    DefaultChatId TEXT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CooldownMinutes INTEGER NOT NULL DEFAULT 15,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_TelegramConnections_IsActive
                    ON TelegramConnections (IsActive);

                CREATE TABLE IF NOT EXISTS TelegramRecipients (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ConnectionId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    ChatId TEXT NOT NULL,
                    DestinationType TEXT NOT NULL DEFAULT 'user',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_TelegramRecipients_ConnectionId
                    ON TelegramRecipients (ConnectionId);
                CREATE INDEX IF NOT EXISTS IX_TelegramRecipients_IsActive
                    ON TelegramRecipients (IsActive);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureSystemSettingsSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS SystemSettings (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """;
            createCommand.ExecuteNonQuery();

            using var seedCommand = connection.CreateCommand();
            seedCommand.CommandText = """
                INSERT INTO SystemSettings (Key, Value, UpdatedAt)
                SELECT 'TimeZoneId', 'America/Sao_Paulo', datetime('now')
                WHERE NOT EXISTS (SELECT 1 FROM SystemSettings WHERE Key = 'TimeZoneId');
                """;
            seedCommand.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureUserSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info('Users')";
            using var reader = checkCommand.ExecuteReader();
            var hasPermissions = false;
            var hasMfaRequired = false;
            var hasMfaEnabled = false;
            var hasMfaSecret = false;
            while (reader.Read())
            {
                var columnName = reader["name"]?.ToString();
                if (string.Equals(columnName, "Permissions", StringComparison.OrdinalIgnoreCase))
                {
                    hasPermissions = true;
                }
                if (string.Equals(columnName, "MfaRequired", StringComparison.OrdinalIgnoreCase))
                {
                    hasMfaRequired = true;
                }
                if (string.Equals(columnName, "MfaEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    hasMfaEnabled = true;
                }
                if (string.Equals(columnName, "MfaSecret", StringComparison.OrdinalIgnoreCase))
                {
                    hasMfaSecret = true;
                }
            }

            if (!hasPermissions)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE Users ADD COLUMN Permissions TEXT NOT NULL DEFAULT ''";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasMfaEnabled)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE Users ADD COLUMN MfaEnabled INTEGER NOT NULL DEFAULT 0";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasMfaRequired)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE Users ADD COLUMN MfaRequired INTEGER NOT NULL DEFAULT 0";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasMfaSecret)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE Users ADD COLUMN MfaSecret TEXT NOT NULL DEFAULT ''";
                alterCommand.ExecuteNonQuery();
            }

            using var normalizeRolesCommand = connection.CreateCommand();
            normalizeRolesCommand.CommandText = """
                UPDATE Users SET Role = 'user' WHERE lower(Role) IN ('viewer', 'operator');
                UPDATE Users SET Role = 'custom' WHERE lower(Role) = 'supervisor';
                """;
            normalizeRolesCommand.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureTagConfigSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info('TagConfigs')";
            using var reader = checkCommand.ExecuteReader();
            var hasMqttConnectionId = false;
            var hasOpcuaConnectionId = false;
            var hasFolderId = false;
            var hasPersistenceMode = false;
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), "MqttConnectionId", StringComparison.OrdinalIgnoreCase))
                {
                    hasMqttConnectionId = true;
                }

                if (string.Equals(reader["name"]?.ToString(), "OpcuaConnectionId", StringComparison.OrdinalIgnoreCase))
                {
                    hasOpcuaConnectionId = true;
                }

                if (string.Equals(reader["name"]?.ToString(), "FolderId", StringComparison.OrdinalIgnoreCase))
                {
                    hasFolderId = true;
                }

                if (string.Equals(reader["name"]?.ToString(), "PersistenceMode", StringComparison.OrdinalIgnoreCase))
                {
                    hasPersistenceMode = true;
                }
            }

            if (!hasMqttConnectionId)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TagConfigs ADD COLUMN MqttConnectionId INTEGER NULL";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasOpcuaConnectionId)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TagConfigs ADD COLUMN OpcuaConnectionId INTEGER NULL";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasFolderId)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TagConfigs ADD COLUMN FolderId INTEGER NULL";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasPersistenceMode)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TagConfigs ADD COLUMN PersistenceMode TEXT NOT NULL DEFAULT 'mes'";
                alterCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureTagRuntimeSnapshotSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS TagRuntimeSnapshots (
                    TagId INTEGER NOT NULL PRIMARY KEY,
                    ValueJson TEXT NOT NULL,
                    Quality TEXT NOT NULL,
                    SourceTimestamp TEXT NOT NULL,
                    LastPersistedAt TEXT NOT NULL
                )
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureAlertRuleSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS AlertRules (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    TagConfigId INTEGER NOT NULL,
                    Operator TEXT NOT NULL,
                    LimitValue REAL NOT NULL,
                    Severity TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    TelegramConnectionId INTEGER NULL,
                    TelegramRecipientIds TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """;
            command.ExecuteNonQuery();

            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info('AlertRules')";
            using var reader = checkCommand.ExecuteReader();
            var hasTelegramConnectionId = false;
            var hasTelegramRecipientIds = false;
            while (reader.Read())
            {
                var columnName = reader["name"]?.ToString();
                if (string.Equals(columnName, "TelegramConnectionId", StringComparison.OrdinalIgnoreCase))
                {
                    hasTelegramConnectionId = true;
                }
                if (string.Equals(columnName, "TelegramRecipientIds", StringComparison.OrdinalIgnoreCase))
                {
                    hasTelegramRecipientIds = true;
                }
            }

            if (!hasTelegramConnectionId)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE AlertRules ADD COLUMN TelegramConnectionId INTEGER NULL";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasTelegramRecipientIds)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE AlertRules ADD COLUMN TelegramRecipientIds TEXT NOT NULL DEFAULT ''";
                alterCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsurePendingMySqlEnvelopeSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PendingMySqlEnvelopes (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    PayloadJson TEXT NOT NULL,
                    Attempts INTEGER NOT NULL,
                    NextAttemptAt TEXT NOT NULL,
                    LastError TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    ProcessedAt TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PendingMySqlEnvelopes_Pending
                    ON PendingMySqlEnvelopes (ProcessedAt, NextAttemptAt, Id);
                CREATE INDEX IF NOT EXISTS IX_PendingMySqlEnvelopes_ProcessedAt
                    ON PendingMySqlEnvelopes (ProcessedAt);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureMachineDowntimeReasonSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS MachineDowntimeReasons (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    MachineId TEXT NOT NULL,
                    Code INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    Category TEXT NULL,
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_MachineDowntimeReasons_MachineId_Code
                    ON MachineDowntimeReasons (MachineId, Code);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureMachineOeeConfigSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info('MachineOEEConfigs')";
            using var reader = checkCommand.ExecuteReader();
            var hasLossSource = false;
            var hasFixedLossValue = false;
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), "LossSource", StringComparison.OrdinalIgnoreCase))
                {
                    hasLossSource = true;
                }

                if (string.Equals(reader["name"]?.ToString(), "FixedLossValue", StringComparison.OrdinalIgnoreCase))
                {
                    hasFixedLossValue = true;
                }
            }

            if (!hasLossSource)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE MachineOEEConfigs ADD COLUMN LossSource TEXT NOT NULL DEFAULT 'tag'";
                alterCommand.ExecuteNonQuery();
            }

            if (!hasFixedLossValue)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE MachineOEEConfigs ADD COLUMN FixedLossValue REAL NOT NULL DEFAULT 0";
                alterCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureMachineFolderSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS MachineFolders (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        ParentFolderId INTEGER NULL,
                        IsSector INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_MachineFolders_ParentFolderId_Name
                        ON MachineFolders (ParentFolderId, Name);
                    """;
                command.ExecuteNonQuery();
            }

            var hasFolderId = false;
            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = "PRAGMA table_info('Machines')";
                using var reader = checkCommand.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "FolderId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFolderId = true;
                    }
                }
            }

            if (!hasFolderId)
            {
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE Machines ADD COLUMN FolderId INTEGER NULL";
                alterCommand.ExecuteNonQuery();
            }

            var hasIsSector = false;
            using (var folderCheckCommand = connection.CreateCommand())
            {
                folderCheckCommand.CommandText = "PRAGMA table_info('MachineFolders')";
                using var folderReader = folderCheckCommand.ExecuteReader();
                while (folderReader.Read())
                {
                    if (string.Equals(folderReader["name"]?.ToString(), "IsSector", StringComparison.OrdinalIgnoreCase))
                    {
                        hasIsSector = true;
                    }
                }
            }

            if (!hasIsSector)
            {
                using var alterFolderCommand = connection.CreateCommand();
                alterFolderCommand.CommandText = "ALTER TABLE MachineFolders ADD COLUMN IsSector INTEGER NOT NULL DEFAULT 0";
                alterFolderCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureUserSessionSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS UserSessions (
                    SessionId TEXT NOT NULL PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    TenantId TEXT NOT NULL,
                    DeviceId TEXT NOT NULL,
                    DeviceType TEXT NOT NULL,
                    RefreshToken TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL,
                    LastActivityAt TEXT NULL,
                    IsActive INTEGER NOT NULL,
                    IpAddress TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_UserSessions_RefreshToken
                    ON UserSessions (RefreshToken);
                CREATE INDEX IF NOT EXISTS IX_UserSessions_UserId
                    ON UserSessions (UserId);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureAuditLogSchema(DataScadaDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    Username TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    EntityType TEXT NULL,
                    EntityId TEXT NULL,
                    StatusCode INTEGER NOT NULL,
                    IpAddress TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAt
                    ON AuditLogs (CreatedAt);
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void SeedMachines(DataScadaDbContext dbContext)
    {
        if (dbContext.Machines.Any())
        {
            return;
        }

        dbContext.Machines.AddRange(
            new Machine { Name = "Máquina A", Code = "MACH-001", CostCenter = "CC-001", Location = "Setor 1", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Machine { Name = "Máquina B", Code = "MACH-002", CostCenter = "CC-002", Location = "Setor 1", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Machine { Name = "Máquina C", Code = "MACH-003", CostCenter = "CC-003", Location = "Setor 2", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Machine { Name = "Máquina D", Code = "MACH-004", CostCenter = "CC-004", Location = "Setor 2", IsActive = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        );
        dbContext.SaveChanges();
    }

    private static void SeedUsers(DataScadaDbContext dbContext, IConfiguration configuration)
    {
        if (dbContext.Users.Any())
        {
            return;
        }

        var adminPassword = configuration["SeedUsers:AdminPassword"];
        var operatorPassword = configuration["SeedUsers:OperatorPassword"];
        var viewerPassword = configuration["SeedUsers:ViewerPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

        dbContext.Users.Add(new User { Username = "admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword), Role = "admin", Email = "admin@local", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        if (!string.IsNullOrWhiteSpace(operatorPassword))
        {
            dbContext.Users.Add(new User { Username = "operator", PasswordHash = BCrypt.Net.BCrypt.HashPassword(operatorPassword), Role = "custom", Permissions = """["goals.manage","reports.download","alert-rules.manage"]""", Email = "operator@local", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }

        if (!string.IsNullOrWhiteSpace(viewerPassword))
        {
            dbContext.Users.Add(new User { Username = "viewer", PasswordHash = BCrypt.Net.BCrypt.HashPassword(viewerPassword), Role = "user", Email = "viewer@local", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }

        dbContext.SaveChanges();
    }

    private static void SeedMySqlConfig(DataScadaDbContext dbContext, IConfiguration configuration)
    {
        if (dbContext.MySqlConfigs.Any())
        {
            return;
        }

        var user = configuration["BootstrapMySql:User"];
        var password = configuration["BootstrapMySql:Password"];
        var database = configuration["BootstrapMySql:Database"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        dbContext.MySqlConfigs.Add(new MySqlConfig
        {
            Provider = configuration["BootstrapMySql:Provider"] ?? "MySQL",
            Name = configuration["BootstrapMySql:Name"] ?? "MySQL Local MES",
            Host = configuration["BootstrapMySql:Host"] ?? "localhost",
            Port = int.TryParse(configuration["BootstrapMySql:Port"], out var port) ? port : 3306,
            User = user,
            Password = password,
            Database = database,
            PoolSize = 10,
            IsActive = true,
            IsPrimary = true,
            IsLocal = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }

    private static void BackfillMqttTagConnections(DataScadaDbContext dbContext)
    {
        var firstMqttConfigId = dbContext.MqttConfigs
            .Where(config => config.IsActive)
            .OrderBy(config => config.Id)
            .Select(config => (int?)config.Id)
            .FirstOrDefault();

        if (!firstMqttConfigId.HasValue)
        {
            return;
        }

        var mqttTags = dbContext.TagConfigs
            .Where(tag => tag.DriverType.ToUpper() == "MQTT" && tag.MqttConnectionId == null)
            .ToList();

        if (mqttTags.Count == 0)
        {
            return;
        }

        foreach (var tag in mqttTags)
        {
            tag.MqttConnectionId = firstMqttConfigId.Value;
            tag.UpdatedAt = DateTime.UtcNow;
        }

        dbContext.SaveChanges();
    }

    private static void BackfillOpcuaTagConnections(DataScadaDbContext dbContext)
    {
        var firstOpcuaConfigId = dbContext.OpcuaConfigs
            .Where(config => config.IsActive)
            .OrderBy(config => config.Id)
            .Select(config => (int?)config.Id)
            .FirstOrDefault();

        if (!firstOpcuaConfigId.HasValue)
        {
            return;
        }

        var opcuaTags = dbContext.TagConfigs
            .Where(tag => tag.DriverType.ToUpper() == "OPCUA" && tag.OpcuaConnectionId == null)
            .ToList();

        if (opcuaTags.Count == 0)
        {
            return;
        }

        foreach (var tag in opcuaTags)
        {
            tag.OpcuaConnectionId = firstOpcuaConfigId.Value;
            tag.UpdatedAt = DateTime.UtcNow;
        }

        dbContext.SaveChanges();
    }
}
