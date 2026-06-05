using System.Security.Claims;

namespace Scada.Security.Interfaces;

public interface ITokenService
{
    string GenerateToken(string userId, string username, string role, string tenantId, List<string>? permissions = null);
    string GenerateToken(string userId, string username, string role);
    bool ValidateToken(string token);
    ClaimsPrincipal? GetPrincipalFromToken(string token);
    string? GetTenantIdFromToken(string token);
    List<string> GetPermissionsFromToken(string token);
}
