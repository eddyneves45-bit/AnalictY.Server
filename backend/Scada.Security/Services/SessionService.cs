using Scada.Security.Interfaces;
using Scada.Security.Models;

namespace Scada.Security.Services;

public class SessionService : ISessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, UserSession> _sessions;
    private readonly Dictionary<string, List<string>> _userSessions; // userId -> sessionIds

    public SessionService()
    {
        _sessions = new Dictionary<string, UserSession>();
        _userSessions = new Dictionary<string, List<string>>();
    }

    public async Task<UserSession> CreateSessionAsync(string userId, string tenantId, string deviceId, string deviceType, string? ipAddress = null)
    {
        await Task.CompletedTask;

        var sessionId = Guid.NewGuid().ToString();
        var refreshToken = Guid.NewGuid().ToString();

        var session = new UserSession
        {
            SessionId = sessionId,
            UserId = userId,
            TenantId = tenantId,
            DeviceId = deviceId,
            DeviceType = deviceType,
            RefreshToken = refreshToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
            LastActivityAt = DateTime.UtcNow,
            IsActive = true,
            IpAddress = ipAddress
        };

        _sessions[sessionId] = session;

        if (!_userSessions.ContainsKey(userId))
        {
            _userSessions[userId] = new List<string>();
        }
        _userSessions[userId].Add(sessionId);

        return session;
    }

    public async Task<UserSession?> GetSessionAsync(string sessionId)
    {
        await Task.CompletedTask;

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            if (IsUsable(session))
            {
                session.LastActivityAt = DateTime.UtcNow;
                return session;
            }
        }

        return null;
    }

    public async Task<UserSession?> GetSessionByRefreshTokenAsync(string refreshToken)
    {
        await Task.CompletedTask;

        return _sessions.Values.FirstOrDefault(session =>
            IsUsable(session) &&
            session.RefreshToken == refreshToken);
    }

    public async Task<List<UserSession>> GetUserSessionsAsync(string userId)
    {
        await Task.CompletedTask;

        if (_userSessions.TryGetValue(userId, out var sessionIds))
        {
            var sessions = new List<UserSession>();
            foreach (var sessionId in sessionIds)
            {
                if (_sessions.TryGetValue(sessionId, out var session) && IsUsable(session))
                {
                    sessions.Add(session);
                }
            }
            return sessions;
        }

        return new List<UserSession>();
    }

    public async Task<bool> RevokeSessionAsync(string sessionId)
    {
        await Task.CompletedTask;

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.IsActive = false;
            
            if (_userSessions.TryGetValue(session.UserId, out var userSessions))
            {
                userSessions.Remove(sessionId);
            }
            
            return true;
        }

        return false;
    }

    public async Task<bool> RevokeAllUserSessionsAsync(string userId)
    {
        await Task.CompletedTask;

        if (_userSessions.TryGetValue(userId, out var sessionIds))
        {
            foreach (var sessionId in sessionIds)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    session.IsActive = false;
                }
            }
            _userSessions.Remove(userId);
            return true;
        }

        return false;
    }

    public async Task<bool> RotateRefreshTokenAsync(string sessionId, string currentRefreshToken, string newRefreshToken)
    {
        await Task.CompletedTask;

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            if (session.IsActive &&
                IsUsable(session) &&
                session.RefreshToken == currentRefreshToken)
            {
                session.RefreshToken = newRefreshToken;
                session.ExpiresAt = DateTime.UtcNow.Add(SessionLifetime);
                session.LastActivityAt = DateTime.UtcNow;
                return true;
            }
        }

        return false;
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        await Task.CompletedTask;

        var now = DateTime.UtcNow;
        var idleCutoff = now.Subtract(IdleTimeout);
        var expiredSessions = _sessions
            .Where(s => !s.Value.IsActive || s.Value.ExpiresAt < now || s.Value.LastActivityAt <= idleCutoff)
            .Select(s => s.Key)
            .ToList();

        foreach (var sessionId in expiredSessions)
        {
            var session = _sessions[sessionId];
            _sessions.Remove(sessionId);
            
            if (_userSessions.TryGetValue(session.UserId, out var userSessions))
            {
                userSessions.Remove(sessionId);
            }
        }
    }

    private static bool IsUsable(UserSession session)
    {
        var now = DateTime.UtcNow;
        return session.IsActive &&
            session.ExpiresAt > now &&
            session.LastActivityAt > now.Subtract(IdleTimeout);
    }
}
