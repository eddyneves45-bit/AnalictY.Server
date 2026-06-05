namespace Scada.Core.Models.SQLite;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Admin, Supervisor, Operator, Viewer
    public string Permissions { get; set; } = string.Empty;
    public bool MfaRequired { get; set; }
    public bool MfaEnabled { get; set; }
    public string MfaSecret { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
