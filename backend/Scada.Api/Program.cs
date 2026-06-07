using Scada.Core.StateEngine;
using Scada.Core.Quality;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Scada.Security.Configuration;
using Scada.Security.Interfaces;
using Scada.Data.Configuration;
using Scada.Data.Models;
using Scada.Gateway.Configuration;
using Scada.Monitoring.Configuration;
using Scada.Drivers.Configuration;
using Scada.Api.Services;
using Scada.Api.Data;
using Scada.Api.Realtime;
using Scada.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var recentLogStore = new RecentLogStore();
builder.Services.AddSingleton<IRecentLogStore>(recentLogStore);
builder.Logging.AddProvider(new RecentLogProvider(recentLogStore));
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
});
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
});
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Duration;
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["http://localhost:3000", "http://localhost:3001", "https://analicty", "https://analicty.local"];

        policy.SetIsOriginAllowed(origin => IsAllowedCorsOrigin(origin, allowedOrigins))
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ResolveClientIp(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Add Scada Core services
builder.Services.AddSingleton<StateEngineManager>();
builder.Services.AddSingleton<QualityProcessor>();

// Add modular services
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key nao configurada. Defina Jwt__Key no ambiente ou em um arquivo .env local antes de iniciar a API.");
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ScadaApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ScadaClient";

builder.Services.AddSecurityModule(jwtKey, jwtIssuer, jwtAudience);

var dataDirectory = builder.Configuration["AnalictY:DataDirectory"]
    ?? Environment.GetEnvironmentVariable("ANALICTY_DATA")
    ?? Directory.GetCurrentDirectory();
dataDirectory = Path.GetFullPath(dataDirectory);
Directory.CreateDirectory(dataDirectory);
builder.Configuration["AnalictY:DataDirectory"] = dataDirectory;

var dbPath = Path.Combine(dataDirectory, "scada.db");
var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
{
    DataSource = dbPath
}.ToString();
builder.Services.AddDataModule(connectionString);

builder.Services.AddGatewayModule();
builder.Services.AddMonitoringModule();
builder.Services.AddDriversModule();
builder.Services.AddScoped<IOeeApplicationService, OeeApplicationService>();
builder.Services.AddSingleton<IOpcuaSessionFactory, OpcuaSessionFactory>();
builder.Services.AddScoped<IOpcuaConfigService, OpcuaConfigService>();
builder.Services.AddScoped<IMqttConfigService, MqttConfigService>();
builder.Services.AddScoped<IMySqlConfigService, MySqlConfigService>();
builder.Services.AddScoped<ITagConfigService, TagConfigService>();
builder.Services.AddScoped<IConfigApplicationService, ConfigApplicationService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAlertRuleService, AlertRuleService>();
builder.Services.AddScoped<IDowntimeService, DowntimeService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBiService, BiService>();
builder.Services.AddScoped<IMachineService, MachineService>();
builder.Services.AddScoped<IMachineGoalService, MachineGoalService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IMesSummaryService, MesSummaryService>();
builder.Services.AddScoped<ISystemTimeService, SystemTimeService>();
builder.Services.AddScoped<IVirtualMachineService, VirtualMachineService>();
builder.Services.AddSingleton<IVirtualMachineRuntimeService, VirtualMachineRuntimeService>();
builder.Services.AddScoped<IRuntimeService, RuntimeService>();
builder.Services.AddScoped<IStateService, StateService>();
builder.Services.AddScoped<IGatewayAppService, GatewayAppService>();
builder.Services.AddScoped<IDriverStatusService, DriverStatusService>();
builder.Services.AddScoped<IMonitoringAppService, MonitoringAppService>();
builder.Services.AddSingleton<IMqttRuntimeMonitor, MqttRuntimeMonitor>();
builder.Services.AddSingleton<ITagValueQueue, TagValueQueue>();
builder.Services.AddSingleton<IIndustrialHeartbeatService, IndustrialHeartbeatService>();
builder.Services.AddSingleton<IIndustrialMetricsService, IndustrialMetricsService>();
builder.Services.AddSingleton<ITagRuntimeSnapshotStore, TagRuntimeSnapshotStore>();
builder.Services.AddSingleton<ITagHistoryStore, MySqlTagHistoryStore>();
builder.Services.AddSingleton<IMesEventStore, MySqlMesEventStore>();
builder.Services.AddSingleton<IMySqlPersistenceQueue, MySqlPersistenceQueue>();
builder.Services.AddSingleton<IAlertRuleEvaluator, AlertRuleEvaluator>();
builder.Services.AddSingleton<ITelegramNotificationQueue, TelegramNotificationQueue>();
builder.Services.AddSingleton<ITelegramNotificationService, TelegramNotificationService>();
builder.Services.AddSingleton<IMachineRealtimeService, MachineRealtimeService>();
builder.Services.AddSingleton<IRuntimeRealtimeService, RuntimeRealtimeService>();
builder.Services.AddSingleton<IAlertRealtimeService, AlertRealtimeService>();
builder.Services.AddSingleton<IMesDashboardRealtimeService, MesDashboardRealtimeService>();
builder.Services.AddSingleton<IMqttDiagnosticsRealtimeService, MqttDiagnosticsRealtimeService>();
builder.Services.AddHostedService<TagValueProcessorService>();
builder.Services.AddHostedService<MySqlPersistenceWorker>();
builder.Services.AddHostedService<TelegramNotificationWorker>();
builder.Services.AddHostedService<TagHeartbeatMonitorService>();
builder.Services.AddHostedService<OpcuaTagPollingService>();
builder.Services.AddHostedService<MqttTagSubscriptionService>();
builder.Services.AddHostedService<ReportSchedulerService>();
builder.Services.AddHostedService<VirtualMachineSimulationWorker>();
builder.Services.AddHostedService<MdnsResponderService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token) &&
                    context.Request.Cookies.TryGetValue("access_token", out var accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    AddPermissionPolicy(options, "CanManageGoals", "goals.manage");
    AddPermissionPolicy(options, "CanDownloadReports", "reports.download");
    AddPermissionPolicy(options, "CanManageAlertRules", "alert-rules.manage");
    AddPermissionPolicy(options, "CanManageUsers", "users.manage");
    AddPermissionPolicy(options, "CanViewAudit", "audit.view");
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseWebSockets();
app.UseCors();
app.UseRateLimiter();
app.UseHttpLogging();
app.UseMiddleware<FrontendProxyMiddleware>();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
    context.Response.Headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
    if (context.Request.IsHttps ||
        string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'");

    await next();
});
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var requiresCookieSession =
        (path.StartsWithSegments("/api") ||
            path.StartsWithSegments("/ws") ||
            path.StartsWithSegments("/hubs")) &&
        !path.StartsWithSegments("/api/auth/login") &&
        !path.StartsWithSegments("/api/auth/register") &&
        !path.StartsWithSegments("/api/auth/refresh") &&
        !path.StartsWithSegments("/api/weintek/ingest") &&
        !path.StartsWithSegments("/api/weintek/ping") &&
        !path.StartsWithSegments("/api/system/health") &&
        !path.StartsWithSegments("/api/system/version") &&
        !path.StartsWithSegments("/api/system/updates/check") &&
        !path.StartsWithSegments("/api/health") &&
        !IsAnonymousAdminReadPath(context.Request) &&
        !HttpMethods.IsOptions(context.Request.Method);

    if (!requiresCookieSession || context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    var sessionId = context.Request.Cookies["session_id"];
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        ClearAuthCookies(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var sessionService = context.RequestServices.GetRequiredService<ISessionService>();
    var session = await sessionService.GetSessionAsync(sessionId);
    if (session == null)
    {
        ClearAuthCookies(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    if (IsSensitiveAdminPath(context.Request.Path) &&
        !context.Request.Path.StartsWithSegments("/api/admin") &&
        context.User.Identity?.IsAuthenticated == true &&
        !context.User.IsInRole("admin"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    
    var isApiRequest = path.StartsWithSegments("/api");
    var isWebSocketRequest = path.StartsWithSegments("/ws");
    var isHubRequest = path.StartsWithSegments("/hubs");
    var isAnonymousApiPath =
        path.StartsWithSegments("/api/auth/login") ||
        path.StartsWithSegments("/api/auth/register") ||
        path.StartsWithSegments("/api/auth/refresh") ||
        path.StartsWithSegments("/api/weintek/ingest") ||
        path.StartsWithSegments("/api/weintek/ping") ||
        path.StartsWithSegments("/api/system/health") ||
        path.StartsWithSegments("/api/system/version") ||
        path.StartsWithSegments("/api/system/updates/check") ||
        path.StartsWithSegments("/api/health") ||
        IsAnonymousAdminReadPath(context.Request);

    if ((isApiRequest || isWebSocketRequest || isHubRequest) &&
        !HttpMethods.IsOptions(context.Request.Method) &&
        !(isApiRequest && isAnonymousApiPath) &&
        context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == StatusCodes.Status403Forbidden &&
        context.User.Identity?.IsAuthenticated == true &&
        !context.Items.ContainsKey("AuditLogged"))
    {
        await RecordAuditLogAsync(context, "FORBIDDEN");
    }
});
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var isMutation = HttpMethods.IsPost(context.Request.Method) ||
        HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsDelete(context.Request.Method);
    var isAnonymousMutation =
        context.Request.Path.StartsWithSegments("/api/auth/login") ||
        context.Request.Path.StartsWithSegments("/api/auth/register") ||
        context.Request.Path.StartsWithSegments("/api/auth/refresh") ||
        context.Request.Path.StartsWithSegments("/api/weintek/ingest") ||
        context.Request.Path.StartsWithSegments("/api/weintek/ping") ||
        context.Request.Path.StartsWithSegments("/hubs");

    if (isMutation && !isAnonymousMutation)
    {
        var cookieToken = context.Request.Cookies["csrf_token"];
        var headerToken = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cookieToken) ||
            string.IsNullOrWhiteSpace(headerToken) ||
            !string.Equals(cookieToken, headerToken, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("CSRF token inválido.");
            return;
        }
    }

    await next();
});
app.Use(async (context, next) =>
{
    var isMutation = HttpMethods.IsPost(context.Request.Method) ||
        HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsDelete(context.Request.Method);
    var isGoalMutation = context.Request.Path.StartsWithSegments("/api/machines") &&
        context.Request.Path.Value?.Contains("/goals", StringComparison.OrdinalIgnoreCase) == true;
    var adminOnlyMutation =
        context.Request.Path.StartsWithSegments("/api/config") ||
        context.Request.Path.StartsWithSegments("/api/simulator") ||
        (context.Request.Path.StartsWithSegments("/api/machines") && !isGoalMutation) ||
        context.Request.Path.StartsWithSegments("/api/machine-folders");

    if (isMutation &&
        adminOnlyMutation &&
        !context.User.IsInRole("admin"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    var isMutation = HttpMethods.IsPost(context.Request.Method) ||
        HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsDelete(context.Request.Method);
    if (!isMutation || context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    await next();

    await RecordAuditLogAsync(context, context.Request.Method);
});

DatabaseSeeder.EnsureCreatedAndSeed(app);

if (args.Any(argument => string.Equals(argument, "--bootstrap-admin", StringComparison.OrdinalIgnoreCase)))
{
    app.Logger.LogInformation("Bootstrap admin concluido. Encerrando sem iniciar o servidor HTTP.");
    return;
}

await RuntimeTagSeeder.RegisterConfiguredTagsAsync(app);

app.Use(async (context, next) =>
{
    var startedAt = DateTime.UtcNow;
    await next();
    var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("HttpRequestSummary");
    logger.LogInformation(
        "HTTP {Method} {Path} => {StatusCode} ({ElapsedMs:0.0} ms)",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        elapsedMs);
});

app.MapScadaEndpoints();
app.MapHub<MesHub>("/hubs/mes").RequireAuthorization();

app.Run();

static string? ResolveEntityType(PathString path)
{
    var value = path.Value ?? string.Empty;
    if (value.StartsWith("/api/config/", StringComparison.OrdinalIgnoreCase)) return "config";
    if (value.StartsWith("/api/machines", StringComparison.OrdinalIgnoreCase)) return "machine";
    if (value.StartsWith("/api/machine-folders", StringComparison.OrdinalIgnoreCase)) return "machine_folder";
    if (value.StartsWith("/api/alert-rules", StringComparison.OrdinalIgnoreCase)) return "alert_rule";
    if (value.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)) return "auth";
    return null;
}

static string? ResolveEntityId(PathString path)
{
    return path.Value?
        .Split('/', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(segment => int.TryParse(segment, out _));
}

static async Task RecordAuditLogAsync(HttpContext context, string action)
{
    var dbContext = context.RequestServices.GetRequiredService<ScadaDbContext>();
    dbContext.AuditLogs.Add(new Scada.Core.Models.SQLite.AuditLog
    {
        UserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
        Username = context.User.Identity?.Name ?? string.Empty,
        Role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty,
        Action = action,
        Path = context.Request.Path,
        EntityType = ResolveEntityType(context.Request.Path),
        EntityId = ResolveEntityId(context.Request.Path),
        StatusCode = context.Response.StatusCode,
        IpAddress = ResolveClientIp(context),
        CreatedAt = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync();
    context.Items["AuditLogged"] = true;
}

static string ResolveClientIp(HttpContext context)
{
    var cloudflareIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(cloudflareIp))
    {
        return cloudflareIp.Trim();
    }

    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "unknown";
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static bool IsSensitiveAdminPath(PathString path)
{
    return path.StartsWithSegments("/api/config") ||
        path.StartsWithSegments("/api/logs") ||
        path.StartsWithSegments("/metrics");
}

static bool IsAnonymousAdminReadPath(HttpRequest request)
{
    var path = request.Path;
    
    // Allow GET requests for read operations
    if (HttpMethods.IsGet(request.Method))
    {
        return path.StartsWithSegments("/api/admin/server/overview") ||
            path.StartsWithSegments("/api/admin/runtime/status") ||
            path.StartsWithSegments("/api/admin/database/status") ||
            path.StartsWithSegments("/api/admin/logs") ||
            path.StartsWithSegments("/api/admin/services") ||
            path.StartsWithSegments("/api/admin/tags") ||
            path.StartsWithSegments("/api/admin/mqtt/status") ||
            path.StartsWithSegments("/api/admin/opcua/status") ||
            path.StartsWithSegments("/api/admin/backup/status") ||
            path.StartsWithSegments("/api/admin/backups") ||
            path.StartsWithSegments("/api/admin/local-server/info") ||
            path.StartsWithSegments("/api/admin/events");
    }
    
    // Allow POST for backup creation (temporary for testing)
    if (HttpMethods.IsPost(request.Method))
    {
        return path.StartsWithSegments("/api/admin/backups") ||
               path.StartsWithSegments("/api/admin/backups/") && path.Value?.Contains("/restore") == true;
    }
    
    return false;
}

static bool IsAllowedCorsOrigin(string origin, string[] allowedOrigins)
{
    if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (uri.Scheme != Uri.UriSchemeHttp)
    {
        return false;
    }

    if (uri.Port is < 3000 or > 3005)
    {
        return false;
    }

    return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.StartsWith("10.", StringComparison.OrdinalIgnoreCase) ||
        IsPrivate172Address(uri.Host);
}

static bool IsPrivate172Address(string host)
{
    var parts = host.Split('.');
    return parts.Length == 4 &&
        parts[0] == "172" &&
        int.TryParse(parts[1], out var second) &&
        second is >= 16 and <= 31;
}

static void ClearAuthCookies(HttpContext context)
{
    context.Response.Cookies.Delete("access_token");
    context.Response.Cookies.Delete("refresh_token");
    context.Response.Cookies.Delete("session_id");
    context.Response.Cookies.Delete("csrf_token");
}

static void AddPermissionPolicy(AuthorizationOptions options, string policyName, string permission)
{
    options.AddPolicy(policyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new PermissionAuthorizationRequirement(permission));
    });
}




