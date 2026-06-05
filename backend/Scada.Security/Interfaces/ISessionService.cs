using Scada.Security.Models;

namespace Scada.Security.Interfaces;

public interface ISessionService
{
    Task<UserSession> CreateSessionAsync(string userId, string tenantId, string deviceId, string deviceType, string? ipAddress = null);
    Task<UserSession?> GetSessionAsync(string sessionId);
    Task<UserSession?> GetSessionByRefreshTokenAsync(string refreshToken);
    Task<List<UserSession>> GetUserSessionsAsync(string userId);
    Task<bool> RevokeSessionAsync(string sessionId);
    Task<bool> RevokeAllUserSessionsAsync(string userId);
    Task<bool> RotateRefreshTokenAsync(string sessionId, string currentRefreshToken, string newRefreshToken);
    Task CleanupExpiredSessionsAsync();
}
