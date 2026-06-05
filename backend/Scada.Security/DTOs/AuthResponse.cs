namespace Scada.Security.DTOs;

public record AuthResponse(
    bool Success,
    string? Token = null,
    string? RefreshToken = null,
    string? SessionId = null,
    string? Message = null,
    UserInfo? User = null,
    bool MfaRequired = false
);

public record UserInfo(
    string Id,
    string Username,
    string Email,
    string Role,
    List<string>? Permissions = null,
    bool MfaRequired = false,
    bool MfaEnabled = false
);
