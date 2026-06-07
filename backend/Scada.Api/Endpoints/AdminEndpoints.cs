using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Data.Models;

namespace Scada.Api.Endpoints;

public record ServerOverviewResponse(
    string ServerStatus,
    string Version,
    string Channel,
    string Environment,
    string MachineName,
    string Uptime,
    string ApiStatus,
    string DatabaseStatus,
    string RuntimeStatus,
    string MqttStatus,
    string OpcUaStatus,
    int TagsTotal,
    int TagsActive,
    int TagsStale,
    int EventsQueued,
    string DataDirectory,
    string CheckedAt
);

public record RuntimeStatusResponse(
    string Status,
    string Uptime,
    string Version,
    int EventsQueued,
    string ProcessingRate,
    string LastProcessingAt,
    InternalService[] InternalServices
);

public record InternalService(
    string Name,
    string Status,
    string Uptime
);

public record DatabaseStatusResponse(
    string Status,
    string Provider,
    string DatabaseFile,
    string Size,
    int Tables,
    long Records,
    string LastWriteAt,
    bool BackupAvailable
);

public record LogEntry(
    string DateTime,
    string Level,
    string Service,
    string Message
);

public record LogsResponse(
    LogEntry[] Logs
);

public record ServiceInfo(
    string Name,
    string Status,
    string Uptime,
    int Port,
    int? Pid
);

public record ServicesResponse(
    ServiceInfo[] Services
);

public record TagsSummary(
    int Total,
    int Active,
    int Stale,
    int Error
);

public record TagsResponse(
    TagsSummary Summary,
    object[] Tags
);

public record MqttStatusResponse(
    string Status,
    int Port,
    int ClientsConnected,
    int Topics,
    double MessagesReceivedPerSecond,
    double MessagesSentPerSecond,
    int Retained
);

public record OpcUaServerInfo(
    string Name,
    string Url,
    string Status
);

public record OpcUaStatusResponse(
    string Status,
    OpcUaServerInfo[] Servers,
    int MonitoredNodes,
    string Quality
);

public record BackupInfo(
    string FileName,
    string CreatedAt,
    string Size
);

public record BackupStatusResponse(
    string? LastBackupAt,
    string LastBackupStatus,
    string BackupDirectory,
    bool Scheduled,
    BackupInfo[] Backups
);

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        // GET /api/admin/server/overview
        app.MapGet("/api/admin/server/overview", (
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IRecentLogStore recentLogStore) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var machineName = Environment.MachineName ?? "-";
            var uptime = GetProcessUptime();
            var version = GetAssemblyVersion();
            var channel = "dev";
            var env = environment.EnvironmentName ?? "Unknown";

            // Database status
            var dbPath = System.IO.Path.Combine(dataDirectory, "scada.db");
            var databaseStatus = System.IO.File.Exists(dbPath) ? "Conectado" : "Desconectado";

            // Runtime status
            var runtimeStatus = "Em execução";

            // MQTT status
            var mqttStatus = "Aguardando";

            // OPC UA status
            var opcUaStatus = "Aguardando";

            // Tags summary
            var tagsTotal = 0;
            var tagsActive = 0;
            var tagsStale = 0;

            // Events queued
            var eventsQueued = 0;

            var response = new ServerOverviewResponse(
                ServerStatus: "Online",
                Version: version,
                Channel: channel,
                Environment: env,
                MachineName: machineName,
                Uptime: uptime,
                ApiStatus: "Online",
                DatabaseStatus: databaseStatus,
                RuntimeStatus: runtimeStatus,
                MqttStatus: mqttStatus,
                OpcUaStatus: opcUaStatus,
                TagsTotal: tagsTotal,
                TagsActive: tagsActive,
                TagsStale: tagsStale,
                EventsQueued: eventsQueued,
                DataDirectory: dataDirectory,
                CheckedAt: DateTimeOffset.UtcNow.ToString("o")
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/runtime/status
        app.MapGet("/api/admin/runtime/status", (
            IConfiguration configuration,
            IWebHostEnvironment environment) =>
        {
            var uptime = GetProcessUptime();
            var version = GetAssemblyVersion();
            var eventsQueued = 0;
            var processingRate = "0/s";
            var lastProcessingAt = "-";

            var internalServices = new InternalService[]
            {
                new InternalService("TagValueProcessor", "Em execução", uptime),
                new InternalService("MySqlPersistenceWorker", "Em execução", uptime),
                new InternalService("TelegramNotificationWorker", "Em execução", uptime),
                new InternalService("TagHeartbeatMonitor", "Em execução", uptime),
                new InternalService("OpcuaTagPollingService", "Em execução", uptime),
                new InternalService("MqttTagSubscriptionService", "Em execução", uptime)
            };

            var response = new RuntimeStatusResponse(
                Status: "Em execução",
                Uptime: uptime,
                Version: version,
                EventsQueued: eventsQueued,
                ProcessingRate: processingRate,
                LastProcessingAt: lastProcessingAt,
                InternalServices: internalServices
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/database/status
        app.MapGet("/api/admin/database/status", async (
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ScadaDbContext dbContext) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var dbPath = System.IO.Path.Combine(dataDirectory, "scada.db");
            var status = System.IO.File.Exists(dbPath) ? "Conectado" : "Desconectado";
            var provider = "SQLite";
            var databaseFile = System.IO.File.Exists(dbPath) ? dbPath : "-";
            var size = GetFileSize(dbPath);
            var tables = 0;
            var records = 0L;
            var lastWriteAt = "-";

            if (System.IO.File.Exists(dbPath))
            {
                try
                {
                    tables = await dbContext.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'").FirstOrDefaultAsync();
                    records = await dbContext.Machines.CountAsync() + await dbContext.TagConfigs.CountAsync() + await dbContext.StopEvents.CountAsync();
                    var fileInfo = new System.IO.FileInfo(dbPath);
                    lastWriteAt = fileInfo.LastWriteTimeUtc.ToString("o");
                }
                catch
                {
                    // Fallback on error
                }
            }

            var backupDirectory = System.IO.Path.Combine(dataDirectory, "backups");
            var backupAvailable = System.IO.Directory.Exists(backupDirectory) && System.IO.Directory.GetFiles(backupDirectory).Length > 0;

            var response = new DatabaseStatusResponse(
                Status: status,
                Provider: provider,
                DatabaseFile: databaseFile,
                Size: size,
                Tables: tables,
                Records: records,
                LastWriteAt: lastWriteAt,
                BackupAvailable: backupAvailable
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/logs
        app.MapGet("/api/admin/logs", (IRecentLogStore recentLogStore) =>
        {
            var logs = recentLogStore.GetRecent(50).Select(log => new LogEntry(
                DateTime: log.Timestamp.ToString("o"),
                Level: log.Level,
                Service: log.Category,
                Message: log.Message
            )).ToArray();

            var response = new LogsResponse(
                Logs: logs
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/services
        app.MapGet("/api/admin/services", (IWebHostEnvironment environment) =>
        {
            var uptime = GetProcessUptime();
            var currentProcess = Process.GetCurrentProcess();
            var services = new ServiceInfo[]
            {
                new ServiceInfo(
                    Name: "AnalictY API",
                    Status: "Em execução",
                    Uptime: uptime,
                    Port: 5000,
                    Pid: currentProcess.Id
                )
            };

            var response = new ServicesResponse(
                Services: services
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/tags
        app.MapGet("/api/admin/tags", async (ScadaDbContext dbContext) =>
        {
            var total = await dbContext.TagConfigs.CountAsync();
            var active = 0;
            var stale = 0;
            var error = 0;

            var response = new TagsResponse(
                Summary: new TagsSummary(
                    Total: total,
                    Active: active,
                    Stale: stale,
                    Error: error
                ),
                Tags: Array.Empty<object>()
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/mqtt/status
        app.MapGet("/api/admin/mqtt/status", () =>
        {
            var response = new MqttStatusResponse(
                Status: "Aguardando",
                Port: 1883,
                ClientsConnected: 0,
                Topics: 0,
                MessagesReceivedPerSecond: 0,
                MessagesSentPerSecond: 0,
                Retained: 0
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/opcua/status
        app.MapGet("/api/admin/opcua/status", () =>
        {
            var response = new OpcUaStatusResponse(
                Status: "Aguardando",
                Servers: Array.Empty<OpcUaServerInfo>(),
                MonitoredNodes: 0,
                Quality: "-"
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/backup/status
        app.MapGet("/api/admin/backup/status", (IConfiguration configuration) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var backupDirectory = System.IO.Path.Combine(dataDirectory, "backups");
            var lastBackupAt = (string?)null;
            var lastBackupStatus = "Nenhum backup executado";
            var scheduled = false;
            var backups = Array.Empty<BackupInfo>();

            if (System.IO.Directory.Exists(backupDirectory))
            {
                var files = System.IO.Directory.GetFiles(backupDirectory, "*.db")
                    .Select(path => new System.IO.FileInfo(path))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(10)
                    .Select(f => new BackupInfo(
                        FileName: f.Name,
                        CreatedAt: f.LastWriteTimeUtc.ToString("o"),
                        Size: GetFileSize(f.FullName)
                    ))
                    .ToArray();

                if (files.Length > 0)
                {
                    lastBackupAt = files[0].CreatedAt;
                    lastBackupStatus = "Backup disponível";
                }

                backups = files;
            }

            var response = new BackupStatusResponse(
                LastBackupAt: lastBackupAt,
                LastBackupStatus: lastBackupStatus,
                BackupDirectory: backupDirectory,
                Scheduled: scheduled,
                Backups: backups
            );

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/backups
        app.MapGet("/api/admin/backups", (IConfiguration configuration) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var backupDirectory = System.IO.Path.Combine(dataDirectory, "backups");
            var backups = Array.Empty<BackupInfo>();

            if (System.IO.Directory.Exists(backupDirectory))
            {
                backups = System.IO.Directory.GetFiles(backupDirectory, "*.db")
                    .Select(path => new System.IO.FileInfo(path))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => new BackupInfo(
                        FileName: f.Name,
                        CreatedAt: f.LastWriteTimeUtc.ToString("o"),
                        Size: GetFileSize(f.FullName)
                    ))
                    .ToArray();
            }

            return Results.Ok(new { backups });
        })
        .AllowAnonymous();

        // POST /api/admin/backups
        app.MapPost("/api/admin/backups", async (IConfiguration configuration) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var dbPath = System.IO.Path.Combine(dataDirectory, "scada.db");
            var backupDirectory = System.IO.Path.Combine(dataDirectory, "backups");

            if (!System.IO.File.Exists(dbPath))
            {
                return Results.BadRequest(new { error = "Banco de dados não encontrado" });
            }

            System.IO.Directory.CreateDirectory(backupDirectory);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"backup_{timestamp}.db";
            var backupPath = System.IO.Path.Combine(backupDirectory, backupFileName);

            try
            {
                await Task.Run(() => System.IO.File.Copy(dbPath, backupPath, true));
                
                return Results.Ok(new 
                { 
                    backup_id = backupFileName,
                    status = "completed",
                    message = "Backup criado com sucesso"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Erro ao criar backup: {ex.Message}", statusCode: 500);
            }
        })
        .AllowAnonymous();

        // POST /api/admin/backups/{id}/restore
        app.MapPost("/api/admin/backups/{id}/restore", async (string id, IConfiguration configuration) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var dbPath = System.IO.Path.Combine(dataDirectory, "scada.db");
            var backupDirectory = System.IO.Path.Combine(dataDirectory, "backups");
            var backupPath = System.IO.Path.Combine(backupDirectory, id);

            if (!System.IO.File.Exists(backupPath))
            {
                return Results.BadRequest(new { error = "Backup não encontrado" });
            }

            try
            {
                // Criar backup do arquivo atual antes de restaurar
                if (System.IO.File.Exists(dbPath))
                {
                    var preRestoreBackup = System.IO.Path.Combine(backupDirectory, $"pre_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
                    System.IO.File.Copy(dbPath, preRestoreBackup, true);
                }

                await Task.Run(() => System.IO.File.Copy(backupPath, dbPath, true));
                
                return Results.Ok(new 
                { 
                    status = "success",
                    message = "Backup restaurado com sucesso"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Erro ao restaurar backup: {ex.Message}", statusCode: 500);
            }
        })
        .AllowAnonymous();

        // GET /api/admin/local-server/info
        app.MapGet("/api/admin/local-server/info", (IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var dataDirectory = configuration["AnalictY:DataDirectory"] ?? "-";
            var machineName = Environment.MachineName ?? "-";
            var uptime = GetProcessUptime();
            var version = GetAssemblyVersion();
            var currentProcess = Process.GetCurrentProcess();

            var response = new 
            {
                hostname = machineName,
                ip_address = "127.0.0.1",
                port = 5000,
                os_version = Environment.OSVersion.ToString(),
                cpu_usage_percent = 0,
                memory_usage_mb = currentProcess.WorkingSet64 / (1024 * 1024),
                disk_usage_gb = 0,
                uptime_seconds = (int)(DateTime.Now - currentProcess.StartTime).TotalSeconds,
                data_directory = dataDirectory,
                environment = environment.EnvironmentName
            };

            return Results.Ok(response);
        })
        .AllowAnonymous();

        // GET /api/admin/events
        app.MapGet("/api/admin/events", async (ScadaDbContext dbContext, int limit = 50) =>
        {
            var events = await dbContext.StopEvents
                .OrderByDescending(e => e.StartTime)
                .Take(limit)
                .Select(e => new 
                {
                    id = e.Id.ToString(),
                    timestamp = e.StartTime.ToString("o"),
                    level = "info",
                    source = "downtime",
                    message = e.Reason ?? "Parada registrada",
                    acknowledged = false
                })
                .ToListAsync();

            return Results.Ok(new { events, total = events.Count });
        })
        .AllowAnonymous();

        return app;
    }

    private static string GetProcessUptime()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var uptime = DateTime.Now - process.StartTime;
            if (uptime.TotalHours >= 1)
                return $"{(int)uptime.TotalHours}h {(int)uptime.Minutes}m";
            if (uptime.TotalMinutes >= 1)
                return $"{(int)uptime.TotalMinutes}m {(int)uptime.Seconds}s";
            return $"{(int)uptime.TotalSeconds}s";
        }
        catch
        {
            return "-";
        }
    }

    private static string GetAssemblyVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static string GetFileSize(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return "-";
            var fileInfo = new System.IO.FileInfo(path);
            var bytes = fileInfo.Length;
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024 * 1024)} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024} KB";
            return $"{bytes} B";
        }
        catch
        {
            return "-";
        }
    }
}
