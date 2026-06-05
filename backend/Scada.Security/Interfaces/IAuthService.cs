using Scada.Security.DTOs;

namespace Scada.Security.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> GetCurrentUserAsync(string userId);
    Task<AuthResponse> LogoutAsync(string sessionId);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
}
