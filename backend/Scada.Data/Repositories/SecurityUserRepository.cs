using Microsoft.EntityFrameworkCore;
using Scada.Data.Models;
using Scada.Security.Interfaces;
using Scada.Core.Models.SQLite;

namespace Scada.Data.Repositories;

public class SecurityUserRepository : Scada.Security.Interfaces.IUserRepository
{
    private readonly ScadaDbContext _context;

    public SecurityUserRepository(ScadaDbContext context)
    {
        _context = context;
    }

    public async Task<Scada.Security.Interfaces.UserEntity?> GetByUsernameAsync(string username)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return null;

        return new Scada.Security.Interfaces.UserEntity(
            user.Id.ToString(),
            user.Username,
            user.Email,
            user.PasswordHash,
            user.Role,
            "default", // TenantId padrão (User do Scada.Core não tem TenantId)
            user.IsActive,
            user.Permissions,
            user.MfaRequired,
            user.MfaEnabled,
            user.MfaSecret
        );
    }

    public async Task<Scada.Security.Interfaces.UserEntity?> GetByEmailAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return null;

        return new Scada.Security.Interfaces.UserEntity(
            user.Id.ToString(),
            user.Username,
            user.Email,
            user.PasswordHash,
            user.Role,
            "default", // TenantId padrão
            user.IsActive,
            user.Permissions,
            user.MfaRequired,
            user.MfaEnabled,
            user.MfaSecret
        );
    }

    public async Task<Scada.Security.Interfaces.UserEntity?> GetByIdAsync(string id)
    {
        if (!int.TryParse(id, out int userId))
            return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;

        return new Scada.Security.Interfaces.UserEntity(
            user.Id.ToString(),
            user.Username,
            user.Email,
            user.PasswordHash,
            user.Role,
            "default", // TenantId padrão
            user.IsActive,
            user.Permissions,
            user.MfaRequired,
            user.MfaEnabled,
            user.MfaSecret
        );
    }

    public async Task<Scada.Security.Interfaces.UserEntity> CreateAsync(Scada.Security.Interfaces.UserEntity user)
    {
        var newUser = new User
        {
            Username = user.Username,
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            Role = user.Role,
            Permissions = user.Permissions,
            MfaRequired = user.MfaRequired,
            MfaEnabled = user.MfaEnabled,
            MfaSecret = user.MfaSecret,
            IsActive = user.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        return new Scada.Security.Interfaces.UserEntity(
            newUser.Id.ToString(),
            newUser.Username,
            newUser.Email,
            newUser.PasswordHash,
            newUser.Role,
            "default", // TenantId padrão
            newUser.IsActive,
            newUser.Permissions,
            newUser.MfaRequired,
            newUser.MfaEnabled,
            newUser.MfaSecret
        );
    }

    public async Task UpdateAsync(Scada.Security.Interfaces.UserEntity user)
    {
        if (!int.TryParse(user.Id, out int userId))
            return;

        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (existingUser == null) return;

        existingUser.Username = user.Username;
        existingUser.Email = user.Email;
        existingUser.PasswordHash = user.PasswordHash;
        existingUser.Role = user.Role;
        existingUser.Permissions = user.Permissions;
        existingUser.MfaRequired = user.MfaRequired;
        existingUser.MfaEnabled = user.MfaEnabled;
        existingUser.MfaSecret = user.MfaSecret;
        existingUser.IsActive = user.IsActive;
        existingUser.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }
}
