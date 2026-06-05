using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Security.Interfaces;
using System.Text.Json;

public static class UserEndpoints
{
    private static readonly HashSet<string> AllowedCustomPermissions = new(StringComparer.Ordinal)
    {
        "goals.manage",
        "reports.download",
        "alert-rules.manage",
        "users.manage",
        "audit.view"
    };

    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var users = await dbContext.Users
                .AsNoTracking()
                .OrderBy(user => user.Username)
                .Select(user => new
                {
                    user.Id,
                    user.Username,
                    user.Email,
                    user.Role,
                    permissions = ParsePermissions(user.Permissions),
                    user.IsActive,
                    user.MfaRequired,
                    user.MfaEnabled,
                    user.CreatedAt,
                    user.UpdatedAt
                })
                .ToListAsync(cancellationToken);
            return Results.Ok(users);
        })
        .RequireAuthorization("CanManageUsers");

        app.MapPost("/api/users", async (
            AdminUserRequest request,
            IPasswordService passwordService,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePassword(request.password);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            if (!IsValidRole(request.role)) return Results.BadRequest(new { message = "Papel inválido." });
            if (await dbContext.Users.AnyAsync(user => user.Username == request.username, cancellationToken))
                return Results.BadRequest(new { message = "Usuário já existe." });
            if (await dbContext.Users.AnyAsync(user => user.Email == request.email, cancellationToken))
                return Results.BadRequest(new { message = "Email já cadastrado." });

            var user = new User
            {
                Username = request.username.Trim(),
                Email = request.email.Trim(),
                PasswordHash = passwordService.HashPassword(request.password),
                Role = NormalizeRole(request.role),
                Permissions = SerializePermissions(request.role, request.permissions),
                MfaRequired = request.mfa_required,
                IsActive = request.is_active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { user.Id, user.Username, user.Email, user.Role, permissions = ParsePermissions(user.Permissions), user.IsActive, user.MfaRequired, user.MfaEnabled });
        })
        .RequireAuthorization("CanManageUsers");

        app.MapPut("/api/users/{id:int}", async (
            int id,
            AdminUserUpdateRequest request,
            IPasswordService passwordService,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidRole(request.role)) return Results.BadRequest(new { message = "Papel inválido." });
            var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (user is null) return Results.NotFound();

            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(request.password))
                {
                    var validation = ValidatePassword(request.password);
                    if (validation is not null) return Results.BadRequest(new { message = validation });
                    user.PasswordHash = passwordService.HashPassword(request.password);
                }

                user.MfaRequired = request.mfa_required;
                user.UpdatedAt = DateTime.UtcNow;
                if (user.MfaRequired)
                {
                    await dbContext.UserSessions
                        .Where(session => session.UserId == user.Id.ToString())
                        .ExecuteDeleteAsync(cancellationToken);
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { user.Id, user.Username, user.Email, user.Role, permissions = ParsePermissions(user.Permissions), user.IsActive, user.MfaRequired, user.MfaEnabled });
            }

            user.Email = request.email.Trim();
            user.Role = NormalizeRole(request.role);
            user.Permissions = SerializePermissions(request.role, request.permissions);
            user.MfaRequired = request.mfa_required;
            user.IsActive = request.is_active;
            if (!string.IsNullOrWhiteSpace(request.password))
            {
                var validation = ValidatePassword(request.password);
                if (validation is not null) return Results.BadRequest(new { message = validation });
                user.PasswordHash = passwordService.HashPassword(request.password);
            }
            user.UpdatedAt = DateTime.UtcNow;
            if (user.MfaRequired)
            {
                await dbContext.UserSessions
                    .Where(session => session.UserId == user.Id.ToString())
                    .ExecuteDeleteAsync(cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { user.Id, user.Username, user.Email, user.Role, permissions = ParsePermissions(user.Permissions), user.IsActive, user.MfaRequired, user.MfaEnabled });
        })
        .RequireAuthorization("CanManageUsers");

        app.MapDelete("/api/users/{id:int}", async (
            int id,
            HttpContext context,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var currentUserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.Equals(currentUserId, id.ToString(), StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Não é possível excluir o próprio usuário." });
            }

            var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (user is null) return Results.NotFound();

            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "Usuário admin não pode ser excluído." });
            }

            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"));

        return app;
    }

    private static bool IsValidRole(string role) => NormalizeRole(role) is "admin" or "custom" or "user";

    private static string NormalizeRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "admin" => "admin",
            "custom" => "custom",
            _ => "user"
        };

    private static string SerializePermissions(string role, List<string>? permissions)
    {
        if (NormalizeRole(role) != "custom") return string.Empty;

        var normalized = (permissions ?? new List<string>())
            .Select(permission => permission.Trim())
            .Where(permission => AllowedCustomPermissions.Contains(permission))
            .Distinct()
            .OrderBy(permission => permission)
            .ToList();
        return JsonSerializer.Serialize(normalized);
    }

    private static List<string> ParsePermissions(string? permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(permissionsJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < 10) return "A senha deve ter pelo menos 10 caracteres";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            return "A senha deve conter maiúscula, minúscula, número e caractere especial";
        return null;
    }
}
