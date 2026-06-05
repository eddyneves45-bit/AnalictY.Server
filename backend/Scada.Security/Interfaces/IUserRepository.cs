namespace Scada.Security.Interfaces;

public interface IUserRepository
{
    Task<UserEntity?> GetByUsernameAsync(string username);
    Task<UserEntity?> GetByEmailAsync(string email);
    Task<UserEntity?> GetByIdAsync(string id);
    Task<UserEntity> CreateAsync(UserEntity user);
    Task UpdateAsync(UserEntity user);
}

public record UserEntity(
    string Id,
    string Username,
    string Email,
    string PasswordHash,
    string Role,
    string TenantId,
    bool IsActive,
    string Permissions = "",
    bool MfaRequired = false,
    bool MfaEnabled = false,
    string MfaSecret = ""
);
