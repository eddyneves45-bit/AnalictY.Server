namespace Scada.Data.DTOs;

public record UserDTO(
    int Id,
    string Username,
    string Email,
    string Role,
    bool IsActive,
    int? TenantId = null
);

public record CreateUserDTO(
    string Username,
    string Email,
    string Password,
    string Role = "user",
    int? TenantId = null
);

public record UpdateUserDTO(
    int Id,
    string? Username = null,
    string? Email = null,
    string? Role = null,
    bool? IsActive = null
);
