using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

public static class WeintekEndpoints
{
    private const string ConfigKey = "Weintek:Config";
    private static readonly ConcurrentDictionary<string, WeintekDiscoveredTag> DiscoveredTags = new();
    private static readonly ConcurrentQueue<WeintekPayloadLog> RecentPayloads = new();
    private static readonly ConcurrentQueue<WeintekGatewayAuditLog> GatewayAudits = new();

    public static WebApplication MapWeintekEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/weintek", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            return Results.Ok(ToPublicConfig(await GetConfigAsync(dbContext, cancellationToken)));
        })
        .WithName("GetWeintekConfig");

        app.MapPut("/api/config/weintek", async (
            WeintekConfigRequest request,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var current = await GetConfigAsync(dbContext, cancellationToken);
            var config = new WeintekGatewayConfig
            {
                name = string.IsNullOrWhiteSpace(request.name) ? "FHDX Weintek" : request.name.Trim(),
                gateway = string.IsNullOrWhiteSpace(request.gateway) ? "FHDX_01" : request.gateway.Trim(),
                fhdx_ip = request.fhdx_ip?.Trim() ?? string.Empty,
                endpoint_path = string.IsNullOrWhiteSpace(request.endpoint_path) ? "/api/weintek/ingest" : request.endpoint_path.Trim(),
                enabled = request.enabled,
                enforce_source_ip = request.enforce_source_ip,
                gateway_token_required = true,
                gateway_token_hash = current.gateway_token_hash,
                gateway_token_prefix = current.gateway_token_prefix,
                gateway_token_created_at = current.gateway_token_created_at,
                last_access_at = current.last_access_at,
                last_source_ip = current.last_source_ip
            };

            await SaveConfigAsync(dbContext, config, cancellationToken);
            return Results.Ok(ToPublicConfig(config));
        })
        .WithName("UpdateWeintekConfig");

        app.MapPost("/api/config/weintek/token", async (
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var token = GenerateGatewayToken();
            config.gateway_token_hash = BCrypt.Net.BCrypt.HashPassword(token);
            config.gateway_token_prefix = token[..Math.Min(token.Length, 16)];
            config.gateway_token_created_at = DateTime.UtcNow;

            await SaveConfigAsync(dbContext, config, cancellationToken);

            return Results.Ok(new
            {
                token,
                config = ToPublicConfig(config)
            });
        })
        .WithName("GenerateWeintekGatewayToken");

        app.MapDelete("/api/config/weintek/token", async (
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            config.gateway_token_hash = "";
            config.gateway_token_prefix = "";
            config.gateway_token_created_at = null;
            config.gateway_token_required = true;

            await SaveConfigAsync(dbContext, config, cancellationToken);
            return Results.Ok(ToPublicConfig(config));
        })
        .WithName("RevokeWeintekGatewayToken");

        app.MapGet("/api/config/weintek/browser", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var discoveredAddresses = DiscoveredTags.Keys.ToList();
            var createdAddresses = await dbContext.TagConfigs
                .AsNoTracking()
                .Where(tag => tag.DriverType == "WEINTEK_HTTP" &&
                    tag.IsActive &&
                    discoveredAddresses.Contains(tag.Address))
                .Select(tag => tag.Address)
                .ToListAsync(cancellationToken);
            var createdAddressSet = createdAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new
            {
                tags = DiscoveredTags.Values
                    .GroupBy(item => item.address, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(item => item.last_seen).First())
                    .OrderBy(item => item.machine)
                    .ThenBy(item => item.cost_center)
                    .ThenBy(item => item.tag)
                    .Take(100)
                    .Select(item => new
                    {
                        item.gateway,
                        item.cost_center,
                        item.machine,
                        item.tag,
                        item.address,
                        item.value,
                        item.data_type,
                        item.first_seen,
                        item.last_seen,
                        item.source_ip,
                        created = createdAddressSet.Contains(item.address)
                    })
                    .ToList(),
                payloads = RecentPayloads
                    .Reverse()
                    .Take(20)
                    .ToList(),
                audits = GatewayAudits
                    .Reverse()
                    .Take(30)
                    .ToList()
            });
        })
        .WithName("GetWeintekBrowser");

        app.MapPost("/api/config/weintek/tags", async (
            WeintekCreateTagRequest request,
            ScadaDbContext dbContext,
            ITagRuntimeService tagRuntimeService,
            IIndustrialHeartbeatService heartbeatService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.tag_name))
            {
                return Results.BadRequest(new { message = "Nome da tag e obrigatorio." });
            }

            if (string.IsNullOrWhiteSpace(request.address))
            {
                return Results.BadRequest(new { message = "Endereco Weintek e obrigatorio." });
            }

            var exists = await dbContext.TagConfigs.AnyAsync(
                item => item.TagName == request.tag_name || item.Address == request.address,
                cancellationToken);
            if (exists)
            {
                return Results.BadRequest(new { message = "Tag ja criada com este nome ou endereco." });
            }

            var tag = new TagConfig
            {
                TagName = request.tag_name.Trim(),
                Address = request.address.Trim(),
                DataType = NormalizeDataType(request.data_type),
                DriverType = "WEINTEK_HTTP",
                PersistenceMode = string.Equals(request.persistence_mode, "telemetry", StringComparison.OrdinalIgnoreCase)
                    ? "telemetry"
                    : "mes",
                PollIntervalMs = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            dbContext.TagConfigs.Add(tag);
            await dbContext.SaveChangesAsync(cancellationToken);

            await tagRuntimeService.RegisterTagAsync(tag.Id, tag.TagName, tag.Address, tag.DriverType, tag.DataType, tag.PollIntervalMs);
            tagRuntimeService.UpdateTagConnectionStatus(tag.Id, true);
            heartbeatService.RegisterTag(tag.Id, tag.TagName, tag.DriverType, null, 0);

            if (DiscoveredTags.TryGetValue(tag.Address, out var discovered))
            {
                tagRuntimeService.UpdateTagValue(tag.Id, discovered.value, "GOOD");
            }

            return Results.Ok(new
            {
                success = true,
                tag = new
                {
                    id = tag.Id,
                    tag_name = tag.TagName,
                    driver_type = tag.DriverType,
                    data_type = tag.DataType,
                    address = tag.Address
                }
            });
        })
        .WithName("CreateWeintekDiscoveredTag");

        app.MapPost("/api/weintek/ping", async (
            HttpContext context,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var authResult = ValidateGatewayRequest(context, config, validateEnabled: true);
            if (!authResult.accepted)
            {
                return authResult.statusCode switch
                {
                    StatusCodes.Status503ServiceUnavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
                    StatusCodes.Status403Forbidden => Results.Forbid(),
                    _ => Results.Unauthorized()
                };
            }

            var now = DateTime.UtcNow;
            config.last_access_at = now;
            config.last_source_ip = authResult.remoteIp;
            await SaveConfigAsync(dbContext, config, cancellationToken);
            AddGatewayAudit(config.gateway, "Ping", authResult.remoteIp, 200, "Ping autenticado");

            return Results.Ok(new
            {
                ok = true,
                gateway = config.gateway,
                received_at = now
            });
        })
        .AllowAnonymous()
        .WithName("PingWeintekGateway");

        app.MapPost("/api/weintek/ingest", async (
            HttpContext context,
            ScadaDbContext dbContext,
            ITagValueQueue tagValueQueue,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("WeintekIngest");
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var authResult = ValidateGatewayRequest(context, config, validateEnabled: true);
            if (!authResult.accepted)
            {
                if (authResult.statusCode == StatusCodes.Status403Forbidden)
                {
                    logger.LogWarning("POST Weintek recusado de {RemoteIp}. Esperado: {ExpectedIp}", authResult.remoteIp, config.fhdx_ip);
                    return Results.Forbid();
                }

                return authResult.statusCode == StatusCodes.Status503ServiceUnavailable
                    ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                    : Results.Unauthorized();
            }

            var remoteIp = authResult.remoteIp;
            WeintekIngestRequest? payload;
            try
            {
                payload = await context.Request.ReadFromJsonAsync<WeintekIngestRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                AddGatewayAudit(config.gateway, "Rejected", remoteIp, 400, "JSON invalido");
                return Results.BadRequest(new { message = "JSON invalido." });
            }

            if (payload == null || payload.tags == null || payload.tags.Count == 0)
            {
                AddGatewayAudit(config.gateway, "Rejected", remoteIp, 400, "Payload sem tags");
                return Results.BadRequest(new { message = "Payload Weintek sem tags." });
            }

            var receivedAt = DateTime.UtcNow;
            var gateway = NormalizeIdentity(payload.gateway, config.gateway);
            var costCenter = NormalizeOptionalIdentity(payload.cost_center);
            var machine = NormalizeIdentity(payload.machine, "UNKNOWN");
            var normalizedTags = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in payload.tags)
            {
                var tagName = NormalizeIdentity(item.Key, "");
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                var value = NormalizeJsonElement(item.Value);
                var address = BuildAddress(gateway, machine, tagName);
                normalizedTags[tagName] = value;

                DiscoveredTags.AddOrUpdate(
                    address,
                    _ => new WeintekDiscoveredTag
                    {
                        gateway = gateway,
                        cost_center = costCenter,
                        machine = machine,
                        tag = tagName,
                        address = address,
                        value = value,
                        data_type = InferDataType(value),
                        first_seen = receivedAt,
                        last_seen = receivedAt,
                        source_ip = remoteIp
                    },
                    (_, existing) =>
                    {
                        existing.gateway = gateway;
                        existing.value = value;
                        existing.data_type = InferDataType(value);
                        existing.cost_center = costCenter;
                        existing.machine = machine;
                        existing.tag = tagName;
                        existing.address = address;
                        existing.last_seen = receivedAt;
                        existing.source_ip = remoteIp;
                        return existing;
                    });
            }

            var addresses = normalizedTags.Keys
                .Select(tag => BuildAddress(gateway, machine, tag))
                .ToList();
            var createdTags = await dbContext.TagConfigs
                .Where(tag => tag.DriverType == "WEINTEK_HTTP" && addresses.Contains(tag.Address))
                .ToListAsync(cancellationToken);

            var queuedTags = 0;
            foreach (var tag in createdTags)
            {
                var discovered = DiscoveredTags.GetValueOrDefault(tag.Address);
                var queued = await tagValueQueue.EnqueueAsync(new TagValueEnvelope(
                    tag.Id,
                    tag.TagName,
                    tag.DriverType,
                    tag.PersistenceMode,
                    null,
                    discovered?.value,
                    "GOOD",
                    receivedAt,
                    receivedAt,
                    tag.Address),
                    cancellationToken);

                if (queued)
                {
                    queuedTags++;
                }
            }

            RecentPayloads.Enqueue(new WeintekPayloadLog
            {
                gateway = gateway,
                cost_center = costCenter,
                machine = machine,
                received_at = receivedAt,
                source_ip = remoteIp,
                tags = normalizedTags
            });
            while (RecentPayloads.Count > 50)
            {
                RecentPayloads.TryDequeue(out _);
            }

            config.last_access_at = receivedAt;
            config.last_source_ip = remoteIp;
            await SaveConfigAsync(dbContext, config, cancellationToken);
            AddGatewayAudit(gateway, "Accepted", remoteIp, 200, $"Tags recebidas: {normalizedTags.Count}");

            return Results.Ok(new
            {
                ok = true,
                received_at = receivedAt,
                discovered_tags = normalizedTags.Count,
                matched_created_tags = createdTags.Count,
                queued_tags = queuedTags
            });
        })
        .AllowAnonymous()
        .WithName("IngestWeintekPayload");

        return app;
    }

    private static async Task<WeintekGatewayConfig> GetConfigAsync(ScadaDbContext dbContext, CancellationToken cancellationToken)
    {
        var value = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == ConfigKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
        {
            return new WeintekGatewayConfig();
        }

        return JsonSerializer.Deserialize<WeintekGatewayConfig>(value) ?? new WeintekGatewayConfig();
    }

    private static async Task SaveConfigAsync(ScadaDbContext dbContext, WeintekGatewayConfig config, CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == ConfigKey, cancellationToken);
        if (setting == null)
        {
            dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = ConfigKey,
                Value = JsonSerializer.Serialize(config),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = JsonSerializer.Serialize(config);
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WeintekGatewayAuthResult ValidateGatewayRequest(HttpContext context, WeintekGatewayConfig config, bool validateEnabled)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (validateEnabled && !config.enabled)
        {
            AddGatewayAudit(config.gateway, "Rejected", remoteIp, 503, "Gateway desativado");
            return new WeintekGatewayAuthResult(false, StatusCodes.Status503ServiceUnavailable, remoteIp);
        }

        if (config.enforce_source_ip &&
            !string.IsNullOrWhiteSpace(config.fhdx_ip) &&
            !string.Equals(config.fhdx_ip, remoteIp, StringComparison.OrdinalIgnoreCase))
        {
            AddGatewayAudit(config.gateway, "Rejected", remoteIp, 403, "IP de origem bloqueado");
            return new WeintekGatewayAuthResult(false, StatusCodes.Status403Forbidden, remoteIp);
        }

        var headerGateway = context.Request.Headers["X-Gateway-Id"].FirstOrDefault();
        var headerToken = context.Request.Headers["X-Gateway-Token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(config.gateway_token_hash))
        {
            AddGatewayAudit(config.gateway, "Rejected", remoteIp, 401, "Token nao configurado");
            return new WeintekGatewayAuthResult(false, StatusCodes.Status401Unauthorized, remoteIp);
        }

        if (string.IsNullOrWhiteSpace(headerGateway) ||
            !string.Equals(headerGateway.Trim(), config.gateway, StringComparison.OrdinalIgnoreCase))
        {
            AddGatewayAudit(headerGateway ?? "", "Rejected", remoteIp, 401, "GatewayId invalido");
            return new WeintekGatewayAuthResult(false, StatusCodes.Status401Unauthorized, remoteIp);
        }

        if (string.IsNullOrWhiteSpace(headerToken) ||
            !BCrypt.Net.BCrypt.Verify(headerToken.Trim(), config.gateway_token_hash))
        {
            AddGatewayAudit(config.gateway, "Rejected", remoteIp, 401, "Token invalido");
            return new WeintekGatewayAuthResult(false, StatusCodes.Status401Unauthorized, remoteIp);
        }

        return new WeintekGatewayAuthResult(true, StatusCodes.Status200OK, remoteIp);
    }

    private static object ToPublicConfig(WeintekGatewayConfig config)
    {
        return new
        {
            config.name,
            config.gateway,
            config.fhdx_ip,
            config.endpoint_path,
            config.enabled,
            config.enforce_source_ip,
            gateway_token_required = true,
            token_configured = !string.IsNullOrWhiteSpace(config.gateway_token_hash),
            token_prefix = config.gateway_token_prefix,
            token_created_at = config.gateway_token_created_at,
            config.last_access_at,
            config.last_source_ip
        };
    }

    private static string GenerateGatewayToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return $"fhdx_live_{token}";
    }

    private static void AddGatewayAudit(string gateway, string eventType, string remoteIp, int statusCode, string reason)
    {
        GatewayAudits.Enqueue(new WeintekGatewayAuditLog
        {
            gateway = gateway,
            event_type = eventType,
            source_ip = remoteIp,
            status_code = statusCode,
            reason = reason,
            created_at = DateTime.UtcNow
        });

        while (GatewayAudits.Count > 100)
        {
            GatewayAudits.TryDequeue(out _);
        }
    }

    private static string BuildAddress(string gateway, string machine, string tag)
    {
        return $"weintek://{SanitizeAddressSegment(gateway)}/{SanitizeAddressSegment(machine)}/{SanitizeAddressSegment(tag)}";
    }

    private static string SanitizeAddressSegment(string value)
    {
        return NormalizeOptionalIdentity(value).Replace(" ", "_");
    }

    private static string NormalizeIdentity(string? value, string fallback)
    {
        var normalized = NormalizeOptionalIdentity(value);
        return string.IsNullOrWhiteSpace(normalized) ? NormalizeOptionalIdentity(fallback) : normalized;
    }

    private static string NormalizeOptionalIdentity(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static object? NormalizeJsonElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static string InferDataType(object? value)
    {
        return value switch
        {
            bool => "Bool",
            int or long => "Int32",
            float or double or decimal => "Double",
            _ => "String"
        };
    }

    private static string NormalizeDataType(string? dataType)
    {
        return dataType switch
        {
            "Bool" or "Int16" or "Int32" or "Int64" or "Float" or "Double" or "String" => dataType,
            _ => "Double"
        };
    }
}

public sealed class WeintekGatewayConfig
{
    public string name { get; set; } = "FHDX Weintek";
    public string gateway { get; set; } = "FHDX_01";
    public string fhdx_ip { get; set; } = "";
    public string endpoint_path { get; set; } = "/api/weintek/ingest";
    public bool enabled { get; set; } = true;
    public bool enforce_source_ip { get; set; } = false;
    public bool gateway_token_required { get; set; } = true;
    public string gateway_token_hash { get; set; } = "";
    public string gateway_token_prefix { get; set; } = "";
    public DateTime? gateway_token_created_at { get; set; }
    public DateTime? last_access_at { get; set; }
    public string last_source_ip { get; set; } = "";
}

public record WeintekConfigRequest(
    string name,
    string gateway,
    string? fhdx_ip,
    string? endpoint_path,
    bool enabled,
    bool enforce_source_ip,
    bool gateway_token_required);

public sealed class WeintekIngestRequest
{
    public string gateway { get; set; } = "";
    public string cost_center { get; set; } = "";
    public string machine { get; set; } = "";
    public string timestamp { get; set; } = "";
    public Dictionary<string, JsonElement> tags { get; set; } = new();
}

public record WeintekCreateTagRequest(
    string tag_name,
    string address,
    string data_type,
    string? persistence_mode);

public sealed record WeintekGatewayAuthResult(
    bool accepted,
    int statusCode,
    string remoteIp);

public sealed class WeintekDiscoveredTag
{
    public string gateway { get; set; } = "";
    public string cost_center { get; set; } = "";
    public string machine { get; set; } = "";
    public string tag { get; set; } = "";
    public string address { get; set; } = "";
    public object? value { get; set; }
    public string data_type { get; set; } = "Double";
    public DateTime first_seen { get; set; }
    public DateTime last_seen { get; set; }
    public string source_ip { get; set; } = "";
}

public sealed class WeintekPayloadLog
{
    public string gateway { get; set; } = "";
    public string cost_center { get; set; } = "";
    public string machine { get; set; } = "";
    public DateTime received_at { get; set; }
    public string source_ip { get; set; } = "";
    public Dictionary<string, object?> tags { get; set; } = new();
}

public sealed class WeintekGatewayAuditLog
{
    public string gateway { get; set; } = "";
    public string event_type { get; set; } = "";
    public string source_ip { get; set; } = "";
    public int status_code { get; set; }
    public string reason { get; set; } = "";
    public DateTime created_at { get; set; }
}
