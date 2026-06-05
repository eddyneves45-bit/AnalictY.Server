using System.Text;
using System.Text.Json;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

public static class ConfigEndpoints
{
    private static readonly Dictionary<string, string> AllowedTimeZones = new(StringComparer.Ordinal)
    {
        ["America/Sao_Paulo"] = "Brasil - Brasília (GMT-3)",
        ["America/Manaus"] = "Brasil - Manaus (GMT-4)",
        ["America/Rio_Branco"] = "Brasil - Rio Branco (GMT-5)",
        ["America/New_York"] = "Estados Unidos - Nova York",
        ["America/Chicago"] = "Estados Unidos - Chicago",
        ["America/Denver"] = "Estados Unidos - Denver",
        ["America/Los_Angeles"] = "Estados Unidos - Los Angeles",
        ["America/Mexico_City"] = "México - Cidade do México",
        ["America/Bogota"] = "Colômbia - Bogotá",
        ["America/Argentina/Buenos_Aires"] = "Argentina - Buenos Aires",
        ["Europe/London"] = "Reino Unido - Londres",
        ["Europe/Lisbon"] = "Portugal - Lisboa",
        ["Europe/Madrid"] = "Espanha - Madrid",
        ["Europe/Berlin"] = "Alemanha - Berlim",
        ["Asia/Tokyo"] = "Japão - Tóquio",
        ["Asia/Shanghai"] = "China - Xangai",
        ["UTC"] = "UTC"
    };

    public static WebApplication MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/system/timezone", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var timeZoneId = await GetSystemSettingAsync(dbContext, "TimeZoneId", "America/Sao_Paulo", cancellationToken);
            return Results.Ok(new
            {
                timeZoneId,
                label = AllowedTimeZones.GetValueOrDefault(timeZoneId, timeZoneId),
                options = AllowedTimeZones.Select(item => new { id = item.Key, label = item.Value }).ToList()
            });
        })
        .WithName("GetSystemTimeZone");

        app.MapGet("/api/config/system/time", async (ISystemTimeService timeService, CancellationToken cancellationToken) =>
        {
            var timeZone = await timeService.GetConfiguredTimeZoneAsync(cancellationToken);
            var utcNow = DateTime.UtcNow;
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);

            return Results.Ok(new
            {
                utcNow,
                localNow,
                timeZoneId = timeZone.Id,
                label = AllowedTimeZones.GetValueOrDefault(timeZone.Id, timeZone.Id)
            });
        })
        .WithName("GetSystemTime");

        app.MapPut("/api/config/system/timezone", async (
            TimeZoneConfigRequest request,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!AllowedTimeZones.ContainsKey(request.timeZoneId))
            {
                return Results.BadRequest(new { message = "Fuso horário inválido." });
            }

            var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == "TimeZoneId", cancellationToken);
            if (setting is null)
            {
                dbContext.SystemSettings.Add(new SystemSetting
                {
                    Key = "TimeZoneId",
                    Value = request.timeZoneId,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Value = request.timeZoneId;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new
            {
                timeZoneId = request.timeZoneId,
                label = AllowedTimeZones[request.timeZoneId]
            });
        })
        .WithName("UpdateSystemTimeZone");

        app.MapGet("/api/config/system/local-server", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var mode = await GetSystemSettingAsync(dbContext, "LocalServer:Mode", "current", cancellationToken);
            var hostIp = await GetSystemSettingAsync(dbContext, "LocalServer:HostIp", "192.168.55.147", cancellationToken);
            var backendPort = await GetSystemSettingAsync(dbContext, "LocalServer:BackendPort", "5000", cancellationToken);
            var frontendPort = await GetSystemSettingAsync(dbContext, "LocalServer:FrontendPort", "3000", cancellationToken);

            return Results.Ok(CreateLocalServerResponse(mode, hostIp, backendPort, frontendPort));
        })
        .WithName("GetLocalServerConfig");

        app.MapPut("/api/config/system/local-server", async (
            LocalServerConfigRequest request,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var mode = NormalizeLocalServerMode(request.mode);
            if (mode is null)
            {
                return Results.BadRequest(new { message = "Modo invalido. Use current ou fixed-ip." });
            }

            var hostIp = (request.hostIp ?? string.Empty).Trim();
            if (mode == "fixed-ip" && !IPAddress.TryParse(hostIp, out _))
            {
                return Results.BadRequest(new { message = "Informe um IP fixo valido, por exemplo 192.168.55.147." });
            }

            var backendPort = request.backendPort <= 0 ? 5000 : request.backendPort;
            var frontendPort = request.frontendPort <= 0 ? 3000 : request.frontendPort;
            if (backendPort is < 1 or > 65535 || frontendPort is < 1 or > 65535)
            {
                return Results.BadRequest(new { message = "Portas devem estar entre 1 e 65535." });
            }

            await UpsertSystemSettingAsync(dbContext, "LocalServer:Mode", mode, cancellationToken);
            await UpsertSystemSettingAsync(dbContext, "LocalServer:HostIp", string.IsNullOrWhiteSpace(hostIp) ? "192.168.55.147" : hostIp, cancellationToken);
            await UpsertSystemSettingAsync(dbContext, "LocalServer:BackendPort", backendPort.ToString(), cancellationToken);
            await UpsertSystemSettingAsync(dbContext, "LocalServer:FrontendPort", frontendPort.ToString(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(CreateLocalServerResponse(mode, hostIp, backendPort.ToString(), frontendPort.ToString()));
        })
        .WithName("UpdateLocalServerConfig");

        app.MapGet("/api/config/opcua/all", async (IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetOpcuaConfigsAsync(cancellationToken));
        })
        .WithName("GetOpcuaConfig");

        app.MapGet("/api/config/shifts", async (
            IShiftService shiftService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await shiftService.ListAsync(cancellationToken));
        })
        .WithName("GetShifts");

        app.MapPut("/api/config/shifts", async (
            HttpContext context,
            IShiftService shiftService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<ShiftRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });

            return (await shiftService.UpsertAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpsertShift");

        app.MapDelete("/api/config/shifts/{id:long}", async (
            long id,
            IShiftService shiftService,
            CancellationToken cancellationToken) =>
        {
            return (await shiftService.DeleteAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteShift");

        app.MapPut("/api/config/opcua", async (HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<OpcuaConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await configService.UpsertOpcuaConfigAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateOpcuaConfig");

        app.MapDelete("/api/config/opcua/{id}", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteOpcuaConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteOpcuaConfig");

        app.MapGet("/api/config/opcua/browse", async (string? node_id, int? connection_id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.BrowseOpcuaAsync(node_id, connection_id, cancellationToken)).ToHttpResult();
        });

        app.MapGet("/api/config/mqtt/all", async (IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetMqttConfigsAsync(cancellationToken));
        })
        .WithName("GetMqttConfig");

        app.MapPut("/api/config/mqtt", async (HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MqttConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await configService.UpsertMqttConfigAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateMqttConfig");

        app.MapPost("/api/config/mqtt/certificates/upload", async (
            HttpContext context,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Envie o certificado como multipart/form-data." });
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            var kind = form["kind"].ToString();

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "Arquivo de certificado não informado." });
            }

            const long maxBytes = 256 * 1024;
            if (file.Length > maxBytes)
            {
                return Results.BadRequest(new { message = "Arquivo muito grande. Limite de 256 KB." });
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".ca",
                ".cer",
                ".crt",
                ".key",
                ".pem"
            };

            if (!allowedExtensions.Contains(extension))
            {
                return Results.BadRequest(new { message = "Use arquivos .pem, .crt, .cer, .ca ou .key." });
            }

            var baseDirectory = configuration["Mqtt:CertificateDirectory"];
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                var dataDirectory = configuration["AnalictY:DataDirectory"];
                baseDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                    ? environment.IsDevelopment()
                        ? Path.Combine(environment.ContentRootPath, "certificates", "mqtt")
                        : "/etc/scada/certs/mqtt"
                    : Path.Combine(dataDirectory, "certs", "mqtt");
            }

            Directory.CreateDirectory(baseDirectory);

            var safeName = CreateSafeCertificateFileName(kind, file.FileName);
            var destinationPath = Path.Combine(baseDirectory, safeName);

            await using (var stream = File.Create(destinationPath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    destinationPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }

            return Results.Ok(new
            {
                path = destinationPath,
                fileName = safeName
            });
        })
        .WithName("UploadMqttCertificate");

        app.MapDelete("/api/config/mqtt/{id}", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteMqttConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteMqttConfig");

        app.MapPost("/api/config/mqtt/{id}/test", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.TestMqttConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("TestMqttConfig");

        app.MapGet("/api/config/mqtt/cache/topics", (int? connection_id, IMqttRuntimeMonitor mqttRuntimeMonitor) =>
        {
            return Results.Ok(new { topics = mqttRuntimeMonitor.GetDiscoveredTopics(connection_id) });
        });

        app.MapGet("/api/config/mqtt/clients", async (int? connection_id, ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var query = dbContext.MqttConfigs.AsNoTracking();
            if (connection_id.HasValue)
            {
                query = query.Where(c => c.Id == connection_id.Value);
            }

            var clients = await query
                .Select(c => new
                {
                    client_id = string.IsNullOrWhiteSpace(c.ClientId) ? $"scada-mqtt-{c.Id}" : c.ClientId,
                    connected = c.IsActive,
                    last_seen = c.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new { clients });
        });

        app.MapPost("/api/config/mqtt/publish", async (HttpContext context, ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MqttPublishRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { error = "Request body is null" });

            var config = await dbContext.MqttConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == body.connection_id, cancellationToken);
            if (config == null) return Results.NotFound(new { error = "Configuração MQTT não encontrada" });

            var driver = new Scada.Drivers.Adapters.MqttDriverAdapter(ToDriverConfig(config, body.qos));
            try
            {
                await driver.ConnectAsync();
                await driver.PublishAsync(body.topic, body.payload);
                return Results.Ok(new { success = true, topic = body.topic });
            }
            finally
            {
                await driver.DisconnectAsync();
            }
        });

        app.MapPost("/api/config/mqtt/subscribe", async (
            HttpContext context,
            ScadaDbContext dbContext,
            IMqttRuntimeMonitor mqttRuntimeMonitor,
            IMqttDiagnosticsRealtimeService mqttDiagnosticsRealtimeService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MqttTopicRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { error = "Request body is null" });
            if (string.IsNullOrWhiteSpace(body.topic)) return Results.BadRequest(new { error = "Informe um tópico MQTT válido" });

            var config = await dbContext.MqttConfigs.FirstOrDefaultAsync(c => c.Id == body.connection_id, cancellationToken);
            if (config == null) return Results.NotFound(new { error = "Configuração MQTT não encontrada" });

            var topics = SplitTopics(config.Topics).ToList();
            if (!topics.Contains(body.topic, StringComparer.OrdinalIgnoreCase))
            {
                topics.Add(body.topic.Trim());
                config.Topics = string.Join(',', topics);
                config.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            mqttRuntimeMonitor.RegisterSubscription(config.Id, body.topic.Trim());
            await mqttDiagnosticsRealtimeService.PublishAsync(config.Id, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                topic = body.topic.Trim(),
                topics,
                message = "Tópico salvo. O subscriber aplica a assinatura no próximo ciclo de atualização."
            });
        });

        app.MapPost("/api/config/mqtt/unsubscribe", async (
            HttpContext context,
            ScadaDbContext dbContext,
            IMqttRuntimeMonitor mqttRuntimeMonitor,
            IMqttDiagnosticsRealtimeService mqttDiagnosticsRealtimeService,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MqttTopicRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { error = "Request body is null" });
            if (string.IsNullOrWhiteSpace(body.topic)) return Results.BadRequest(new { error = "Informe um tópico MQTT válido" });

            var config = await dbContext.MqttConfigs.FirstOrDefaultAsync(c => c.Id == body.connection_id, cancellationToken);
            if (config == null) return Results.NotFound(new { error = "Configuração MQTT não encontrada" });

            var topics = SplitTopics(config.Topics)
                .Where(topic => !string.Equals(topic, body.topic.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            config.Topics = string.Join(',', topics);
            config.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            mqttRuntimeMonitor.UnregisterSubscription(config.Id, body.topic.Trim());
            await mqttDiagnosticsRealtimeService.PublishAsync(config.Id, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                topic = body.topic.Trim(),
                topics,
                message = "Tópico removido. O subscriber aplica a remoção no próximo ciclo de atualização."
            });
        });

        app.MapGet("/api/config/mysql/all", async (IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetMySqlConfigsAsync(cancellationToken));
        })
        .WithName("GetMysqlConfig");

        app.MapPut("/api/config/mysql", async (HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MySqlConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await configService.UpsertMySqlConfigAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateMysqlConfig");

        app.MapDelete("/api/config/mysql/{id}", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteMySqlConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteMysqlConfig");

        app.MapPost("/api/config/mysql/{id}/set-primary", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.SetPrimaryMySqlConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("SetPrimaryMysql");

        app.MapPost("/api/config/mysql/{id}/set-local", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.SetLocalMySqlConfigAsync(id, true, cancellationToken)).ToHttpResult();
        })
        .WithName("SetLocalMysql");

        app.MapPost("/api/config/mysql/{id}/set-remote", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.SetLocalMySqlConfigAsync(id, false, cancellationToken)).ToHttpResult();
        })
        .WithName("SetRemoteMysql");

        app.MapPost("/api/config/mysql/{id}/test", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.TestMySqlConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("TestMysql");

        app.MapPost("/api/config/mysql/test", async (HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MySqlConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest();

            return (await configService.TestMySqlRequestAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("TestMysqlRequest");

        app.MapPost("/api/config/mysql/{id}/init", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.InitMySqlConfigAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("InitMysql");

        app.MapGet("/api/machines/{id}/tag-mapping", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetTagMappingsAsync(id, cancellationToken));
        })
        .WithName("GetTagMapping");

        app.MapPost("/api/machines/{id}/tag-mapping", async (int id, HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<CreateMachineTagMapRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { success = false, message = "Request body is null" });

            return (await configService.CreateTagMappingAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateTagMapping");

        app.MapDelete("/api/machines/{id}/tag-mapping/{role}", async (int id, string role, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteTagMappingAsync(id, role, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteTagMapping");

        app.MapGet("/api/machines/{id}/downtime-reasons", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetMachineDowntimeReasonsAsync(id, cancellationToken));
        })
        .WithName("GetMachineDowntimeReasons");

        app.MapPut("/api/machines/{id}/downtime-reasons", async (int id, HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineDowntimeReasonRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });
            return (await configService.UpsertMachineDowntimeReasonAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpsertMachineDowntimeReason");

        app.MapDelete("/api/machines/{id}/downtime-reasons/{code:int}", async (int id, int code, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteMachineDowntimeReasonAsync(id, code, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteMachineDowntimeReason");

        app.MapGet("/api/machines/{id}/loss-config", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetMachineLossConfigAsync(id, cancellationToken));
        })
        .WithName("GetMachineLossConfig");

        app.MapPut("/api/machines/{id}/loss-config", async (int id, HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<MachineLossConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { message = "Request body is null" });
            return (await configService.UpsertMachineLossConfigAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpsertMachineLossConfig");

        app.MapGet("/api/config/tags", async (IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await configService.GetTagsAsync(cancellationToken));
        })
        .WithName("GetTags");

        app.MapPost("/api/config/tags", async (HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<TagConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { success = false, message = "Request body is null" });

            return (await configService.CreateTagAsync(body, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateTagConfig");

        app.MapPut("/api/config/tags/{id}", async (int id, HttpContext context, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<TagConfigRequest>(cancellationToken);
            if (body == null) return Results.BadRequest(new { success = false, message = "Request body is null" });

            return (await configService.UpdateTagAsync(id, body, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateTagConfig");

        app.MapDelete("/api/config/tags/{id}", async (int id, IConfigApplicationService configService, CancellationToken cancellationToken) =>
        {
            return (await configService.DeleteTagAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteTagConfig");

        return app;
    }

    private static async Task<string> GetSystemSettingAsync(
        ScadaDbContext dbContext,
        string key,
        string fallback,
        CancellationToken cancellationToken)
    {
        return await dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == key)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken) ?? fallback;
    }

    private static async Task UpsertSystemSettingAsync(
        ScadaDbContext dbContext,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
        {
            dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        setting.Value = value;
        setting.UpdatedAt = DateTime.UtcNow;
    }

    private static string? NormalizeLocalServerMode(string mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "current" => "current",
            "fixed-ip" => "fixed-ip",
            "fixedip" => "fixed-ip",
            "fixed" => "fixed-ip",
            _ => null
        };
    }

    private static object CreateLocalServerResponse(string mode, string hostIp, string backendPortValue, string frontendPortValue)
    {
        var normalizedMode = NormalizeLocalServerMode(mode) ?? "current";
        var backendPort = int.TryParse(backendPortValue, out var parsedBackendPort) ? parsedBackendPort : 5000;
        var frontendPort = int.TryParse(frontendPortValue, out var parsedFrontendPort) ? parsedFrontendPort : 3000;
        var frontendHost = normalizedMode == "fixed-ip" ? hostIp : "localhost";
        var apiHost = normalizedMode == "fixed-ip" ? hostIp : "localhost";

        return new
        {
            mode = normalizedMode,
            hostIp,
            backendPort,
            frontendPort,
            frontendUrl = $"http://{frontendHost}:{frontendPort}",
            apiUrl = $"http://{apiHost}:{backendPort}",
            bindAddress = normalizedMode == "fixed-ip" ? "0.0.0.0" : "localhost"
        };
    }

    private static IEnumerable<string> SplitTopics(string topics)
    {
        return (topics ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(topic => !string.IsNullOrWhiteSpace(topic));
    }

    private static string CreateSafeCertificateFileName(string kind, string originalFileName)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var safeKind = SanitizeCertificateName(kind);
        var safeName = SanitizeCertificateName(name);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        return $"{safeKind}-{safeName}-{stamp}{extension}";
    }

    private static string SanitizeCertificateName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) || character == '.')
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "mqtt-cert" : builder.ToString();
    }

    private static Scada.Drivers.DTOs.MqttDriverConfig ToDriverConfig(MqttConfig config, int? qos = null)
    {
        return new Scada.Drivers.DTOs.MqttDriverConfig(
            config.BrokerHost,
            string.IsNullOrWhiteSpace(config.ClientId) ? $"scada-mqtt-{config.Id}" : config.ClientId,
            config.Username,
            config.Password,
            config.TlsEnabled,
            config.BrokerPort,
            config.CaCertPath,
            config.ClientCertPath,
            config.ClientKeyPath,
            qos ?? config.Qos);
    }
}
