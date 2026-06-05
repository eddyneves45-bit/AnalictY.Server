namespace Scada.Core.Models.SQLite;

public class StopEvent
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double? Duration { get; set; } // em segundos
    public string StopType { get; set; } = string.Empty; // MICRO_STOP, STOPPED, SETUP, FAULT, etc.
    public string? Cause { get; set; } // Causa básica
    public string? Reason { get; set; } // Razão detalhada
    
    // Diagnóstico automático de causa
    public string? CauseType { get; set; } // MATERIAL_SHORTAGE, OPERATOR_WAIT, etc.
    public double? Confidence { get; set; } // 0.0 a 1.0
    public string? Evidence { get; set; } // JSON com evidências
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
