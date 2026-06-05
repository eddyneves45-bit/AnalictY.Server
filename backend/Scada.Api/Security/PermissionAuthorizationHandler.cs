using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scada.Data.Models;

namespace Scada.Api.Security;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    private readonly ScadaDbContext _dbContext;

    public PermissionAuthorizationHandler(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionAuthorizationRequirement requirement)
    {
        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new { item.Role, item.IsActive, item.Permissions })
            .FirstOrDefaultAsync();

        if (user == null || !user.IsActive)
        {
            return;
        }

        if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        if (!string.Equals(user.Role, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var permissions = ParseStoredPermissions(user.Permissions);
        if (permissions.Any(item => string.Equals(item, requirement.Permission, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }
    }

    private static IReadOnlyCollection<string> ParseStoredPermissions(string? permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyCollection<string>>(permissionsJson) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
