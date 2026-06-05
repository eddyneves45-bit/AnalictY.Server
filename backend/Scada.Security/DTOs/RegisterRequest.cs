using System.ComponentModel.DataAnnotations;

namespace Scada.Security.DTOs;

public record RegisterRequest(
    [Required, MinLength(3)] string Username,
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    string Role = "User"
);
