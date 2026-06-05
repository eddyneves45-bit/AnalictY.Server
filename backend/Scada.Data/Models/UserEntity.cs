namespace Scada.Data.Models;

public record UserEntity(
    int Id,
    string Username,
    string Email,
    string PasswordHash,
    string Role,
    bool IsActive,
    int? TenantId = null,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default
);
