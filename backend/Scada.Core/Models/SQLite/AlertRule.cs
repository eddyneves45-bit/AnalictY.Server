namespace Scada.Core.Models.SQLite;

public class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TagConfigId { get; set; }
    public string Operator { get; set; } = string.Empty;
    public double LimitValue { get; set; }
    public string Severity { get; set; } = "medium";
    public string Message { get; set; } = string.Empty;
    public int? TelegramConnectionId { get; set; }
    public string TelegramRecipientIds { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
