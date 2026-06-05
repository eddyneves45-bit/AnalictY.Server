namespace Scada.Core.Models.SQLite;

public class MySqlConfig
{
    public int Id { get; set; }
    public string Provider { get; set; } = "MySQL";
    public string Name { get; set; } = "MySQL Principal";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3306;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = "mes_production";
    public int PoolSize { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; } = false;
    public bool IsLocal { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
