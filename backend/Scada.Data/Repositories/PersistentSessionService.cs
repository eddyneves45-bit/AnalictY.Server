using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Security.Interfaces;
using Scada.Security.Models;

namespace Scada.Data.Repositories;

public class PersistentSessionService : ISessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private readonly ScadaDbContext _context;

    public PersistentSessionService(ScadaDbContext context)
    {
        _context = context;
    }

    public async Task<UserSession> CreateSessionAsync(
        string userId,
        string tenantId,
        string deviceId,
        string deviceType,
        string? ipAddress = null)
    {
        var record = new UserSessionRecord
        {
            SessionId = Guid.NewGuid().ToString(),
            UserId = userId,
            TenantId = tenantId,
            DeviceId = deviceId,
            DeviceType = deviceType,
            RefreshToken = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
            LastActivityAt = DateTime.UtcNow,
            IsActive = true,
            IpAddress = ipAddress
        };

        _context.UserSessions.Add(record);
        await _context.SaveChangesAsync();
        return ToModel(record);
    }

    public async Task<UserSession?> GetSessionAsync(string sessionId)
    {
        var record = await _context.UserSessions.FirstOrDefaultAsync(session => session.SessionId == sessionId);
        if (!IsUsable(record)) return null;

        record!.LastActivityAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ToModel(record);
    }

    public async Task<UserSession?> GetSessionByRefreshTokenAsync(string refreshToken)
    {
        var record = await _context.UserSessions.FirstOrDefaultAsync(session => session.RefreshToken == refreshToken);
        return IsUsable(record) ? ToModel(record!) : null;
    }

    public async Task<List<UserSession>> GetUserSessionsAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var idleCutoff = now.Subtract(IdleTimeout);
        return await _context.UserSessions
            .Where(session =>
                session.UserId == userId &&
                session.IsActive &&
                session.ExpiresAt > now &&
                session.LastActivityAt > idleCutoff)
            .Select(session => ToModel(session))
            .ToListAsync();
    }

    public async Task<bool> RevokeSessionAsync(string sessionId)
    {
        var record = await _context.UserSessions.FirstOrDefaultAsync(session => session.SessionId == sessionId);
        if (record == null) return false;

        record.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RevokeAllUserSessionsAsync(string userId)
    {
        var sessions = await _context.UserSessions.Where(session => session.UserId == userId && session.IsActive).ToListAsync();
        if (sessions.Count == 0) return false;

        foreach (var session in sessions) session.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RotateRefreshTokenAsync(string sessionId, string currentRefreshToken, string newRefreshToken)
    {
        var record = await _context.UserSessions.FirstOrDefaultAsync(session =>
            session.SessionId == sessionId &&
            session.RefreshToken == currentRefreshToken);
        if (!IsUsable(record)) return false;

        record!.RefreshToken = newRefreshToken;
        record.ExpiresAt = DateTime.UtcNow.Add(SessionLifetime);
        record.LastActivityAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        var now = DateTime.UtcNow;
        var idleCutoff = now.Subtract(IdleTimeout);
        var expired = await _context.UserSessions
            .Where(session => !session.IsActive || session.ExpiresAt <= now || session.LastActivityAt <= idleCutoff)
            .ToListAsync();
        if (expired.Count == 0) return;

        _context.UserSessions.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }

    private static bool IsUsable(UserSessionRecord? record)
    {
        var now = DateTime.UtcNow;
        return record != null &&
            record.IsActive &&
            record.ExpiresAt > now &&
            record.LastActivityAt > now.Subtract(IdleTimeout);
    }

    private static UserSession ToModel(UserSessionRecord record) => new()
    {
        SessionId = record.SessionId,
        UserId = record.UserId,
        TenantId = record.TenantId,
        DeviceId = record.DeviceId,
        DeviceType = record.DeviceType,
        RefreshToken = record.RefreshToken,
        CreatedAt = record.CreatedAt,
        ExpiresAt = record.ExpiresAt,
        LastActivityAt = record.LastActivityAt,
        IsActive = record.IsActive,
        IpAddress = record.IpAddress
    };
}
