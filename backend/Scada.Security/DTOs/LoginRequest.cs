namespace Scada.Security.DTOs;

public record LoginRequest(string Username, string Password, string? MfaCode = null);
