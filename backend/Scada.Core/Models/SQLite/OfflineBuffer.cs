namespace Scada.Core.Models.SQLite;

public class OfflineBuffer
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public string ConnectionType { get; set; } = string.Empty; // 'mqtt' ou 'opc'
    public string? Topic { get; set; } // Tópico MQTT (se aplicável)
    public string? Tag { get; set; } // Tag OPC (se aplicável)
    public string Payload { get; set; } = string.Empty; // Payload JSON da mensagem
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // Timestamp original
    public bool Processed { get; set; } = false; // Se já foi sincronizado
    public int Priority { get; set; } = 0; // Prioridade (0=normal, 1=crítico)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
