namespace Scada.Core.Models.SQLite;

public class TelegramConnection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BotToken { get; set; } = string.Empty;
    public string? DefaultChatId { get; set; }
    public bool IsActive { get; set; } = true;
    public int CooldownMinutes { get; set; } = 15;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
