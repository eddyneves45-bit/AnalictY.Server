using Scada.Security.Models;

namespace Scada.Security.Interfaces;

public interface ITokenProvider
{
    string GenerateToken(User user, List<string>? permissions = null);
    bool ValidateToken(string token);
    System.Security.Claims.ClaimsPrincipal? GetPrincipalFromToken(string token);
}
