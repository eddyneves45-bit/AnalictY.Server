using Microsoft.EntityFrameworkCore;
using Scada.Core.Drivers;
using Scada.Core.Models.SQLite;
using Scada.Core.Quality;
using Scada.Core.StateEngine;
using Scada.Data.Models;
using Scada.Drivers.Services;
using Scada.Gateway.Interfaces;
using Scada.Monitoring.Interfaces;
using Scada.Security.Interfaces;
using System.Net.WebSockets;
using System.Security.Claims;

public static class AuthEndpoints
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionCookieLifetime = TimeSpan.FromHours(24);

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        // Auth endpoints
        app.MapPost("/api/auth/login", async (
            HttpContext context,
            Scada.Security.DTOs.LoginRequest request,
            IAuthService authService) =>
        {
            var response = await authService.LoginAsync(request);
            if (!response.Success)
            {
                if (response.MfaRequired)
                {
                    return Results.Ok(response);
                }

                return Results.Json(response, statusCode: StatusCodes.Status401Unauthorized);
            }

            WriteAuthCookies(context, response);
            WriteCsrfCookie(context);
            return Results.Ok(ToPublicResponse(response));
        })
        .WithName("Login")
        .RequireRateLimiting("auth")
        .AllowAnonymous();

        app.MapPost("/api/auth/register", async (
            Scada.Security.DTOs.RegisterRequest request,
            IAuthService authService,
            IWebHostEnvironment environment,
            IConfiguration configuration) =>
        {
            var allowPublicRegistration = configuration.GetValue<bool?>("Auth:AllowPublicRegistration")
                ?? environment.IsDevelopment();
            if (!allowPublicRegistration)
            {
                return Results.Json(
                    new { success = false, message = "Cadastro público desabilitado em produção." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var viewerRequest = request with { Role = "user" };
            var response = await authService.RegisterAsync(viewerRequest);
            return response.Success ? Results.Ok(response) : Results.BadRequest(response);
        })
        .WithName("Register")
        .RequireRateLimiting("auth")
        .AllowAnonymous();

        app.MapPost("/api/auth/create-admin", async (RegisterRequest request, IAuthService authService) =>
        {
            var adminRequest = new Scada.Security.DTOs.RegisterRequest(
                request.username,
                request.email,
                request.password,
                "admin"
            );
            var response = await authService.RegisterAsync(adminRequest);
            return response.Success ? Results.Ok(response) : Results.BadRequest(response);
        })
        .WithName("CreateAdmin")
        .RequireAuthorization(policy => policy.RequireRole("admin"));

        app.MapGet("/api/auth/me", async (HttpContext context, IAuthService authService) =>
        {
            var userIdClaim = context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Results.Unauthorized();
            }
    
            var response = await authService.GetCurrentUserAsync(userIdClaim);
            return response.Success ? Results.Ok(response) : Results.NotFound(response);
        })
        .WithName("GetCurrentUser")
        .RequireAuthorization();

        app.MapPost("/api/auth/refresh", async (HttpContext context, IAuthService authService) =>
        {
            if (!context.Request.Cookies.TryGetValue("refresh_token", out var refreshToken) ||
                string.IsNullOrWhiteSpace(refreshToken))
            {
                return Results.Unauthorized();
            }

            var response = await authService.RefreshTokenAsync(refreshToken);
            if (!response.Success)
            {
                ClearAuthCookies(context);
                return Results.Json(response, statusCode: StatusCodes.Status401Unauthorized);
            }

            WriteAuthCookies(context, response);
            WriteCsrfCookie(context);
            return Results.Ok(ToPublicResponse(response));
        })
        .WithName("RefreshToken")
        .RequireRateLimiting("auth")
        .AllowAnonymous();

        app.MapPost("/api/auth/logout", async (HttpContext context, IAuthService authService) =>
        {
            var sessionId = context.Request.Cookies["session_id"];
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await authService.LogoutAsync(sessionId);
            }

            ClearAuthCookies(context);
            context.Response.Cookies.Delete("csrf_token");
            return Results.Ok(new { success = true });
        })
        .WithName("Logout")
        .RequireAuthorization();

        app.MapGet("/api/auth/mfa/status", async (
            HttpContext context,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var user = await GetCurrentUserAsync(context, dbContext, cancellationToken);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new { enabled = user.MfaEnabled });
        })
        .WithName("GetMfaStatus")
        .RequireAuthorization();

        app.MapPost("/api/auth/mfa/setup", async (
            HttpContext context,
            ScadaDbContext dbContext,
            ITotpService totpService,
            CancellationToken cancellationToken) =>
        {
            var user = await GetCurrentUserAsync(context, dbContext, cancellationToken);
            if (user is null) return Results.Unauthorized();

            var secret = totpService.GenerateSecret();
            user.MfaSecret = secret;
            user.MfaEnabled = false;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                secret,
                otpauthUrl = totpService.BuildOtpAuthUri("iiOT AnalictY", user.Username, secret)
            });
        })
        .WithName("SetupMfa")
        .RequireAuthorization();

        app.MapPost("/api/auth/mfa/enable", async (
            HttpContext context,
            MfaCodeRequest request,
            ScadaDbContext dbContext,
            ITotpService totpService,
            CancellationToken cancellationToken) =>
        {
            var user = await GetCurrentUserAsync(context, dbContext, cancellationToken);
            if (user is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(user.MfaSecret))
            {
                return Results.BadRequest(new { message = "Configure o MFA antes de ativar." });
            }

            if (!totpService.VerifyCode(user.MfaSecret, request.code))
            {
                return Results.BadRequest(new { message = "Código MFA inválido." });
            }

            user.MfaEnabled = true;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.UserSessions
                .Where(session => session.UserId == user.Id.ToString())
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            ClearAuthCookies(context);
            context.Response.Cookies.Delete("csrf_token");
            return Results.Ok(new { enabled = true, loginRequired = true });
        })
        .WithName("EnableMfa")
        .RequireAuthorization();

        app.MapPost("/api/auth/mfa/disable", async (
            HttpContext context,
            MfaCodeRequest request,
            ScadaDbContext dbContext,
            ITotpService totpService,
            CancellationToken cancellationToken) =>
        {
            var user = await GetCurrentUserAsync(context, dbContext, cancellationToken);
            if (user is null) return Results.Unauthorized();
            if (user.MfaRequired)
            {
                return Results.BadRequest(new { message = "MFA obrigatório para este usuário." });
            }

            if (user.MfaEnabled && !totpService.VerifyCode(user.MfaSecret, request.code))
            {
                return Results.BadRequest(new { message = "Código MFA inválido." });
            }

            user.MfaEnabled = false;
            user.MfaSecret = string.Empty;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { enabled = false });
        })
        .WithName("DisableMfa")
        .RequireAuthorization();

        return app;
    }

    private static void WriteAuthCookies(HttpContext context, Scada.Security.DTOs.AuthResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Token) ||
            string.IsNullOrWhiteSpace(response.RefreshToken) ||
            string.IsNullOrWhiteSpace(response.SessionId))
        {
            return;
        }

        var secure = ShouldUseSecureCookies(context);
        context.Response.Cookies.Append(
            "access_token",
            response.Token,
            CreateCookieOptions(secure, DateTimeOffset.UtcNow.Add(AccessTokenLifetime)));
        context.Response.Cookies.Append(
            "refresh_token",
            response.RefreshToken,
            CreateCookieOptions(secure, DateTimeOffset.UtcNow.Add(SessionCookieLifetime)));
        context.Response.Cookies.Append(
            "session_id",
            response.SessionId,
            CreateCookieOptions(secure, DateTimeOffset.UtcNow.Add(SessionCookieLifetime)));
    }

    private static void ClearAuthCookies(HttpContext context)
    {
        context.Response.Cookies.Delete("access_token");
        context.Response.Cookies.Delete("refresh_token");
        context.Response.Cookies.Delete("session_id");
    }

    private static void WriteCsrfCookie(HttpContext context)
    {
        var secure = ShouldUseSecureCookies(context);
        context.Response.Cookies.Append(
            "csrf_token",
            Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            new CookieOptions
            {
                HttpOnly = false,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.Add(SessionCookieLifetime)
            });
    }

    private static bool ShouldUseSecureCookies(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool?>("Auth:CookieSecure") ?? context.Request.IsHttps;
    }

    private static Scada.Security.DTOs.AuthResponse ToPublicResponse(Scada.Security.DTOs.AuthResponse response)
    {
        return response with
        {
            Token = null,
            RefreshToken = null,
            SessionId = null
        };
    }

    private static CookieOptions CreateCookieOptions(bool secure, DateTimeOffset expiresAt)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expiresAt
        };
    }

    private static async Task<User?> GetCurrentUserAsync(
        HttpContext context,
        ScadaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdValue, out var userId))
        {
            return null;
        }

        return await dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    private sealed record MfaCodeRequest(string code);
}
