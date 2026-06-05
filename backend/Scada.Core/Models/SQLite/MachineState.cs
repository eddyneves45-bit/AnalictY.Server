namespace Scada.Core.Models.SQLite;

public class MachineState
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty; // RUNNING, STOPPED, IDLE
    public string Context { get; set; } = string.Empty; // NONE, FAULT, EMERGENCY, MAINTENANCE, SETUP, NO_DEMAND
    public DateTime StateStartTime { get; set; } = DateTime.UtcNow;
    public int LastStatusWord { get; set; } = 0;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    
    // Métricas adicionais (sistema antigo)
    public DateTime? LastStatusChange { get; set; }
    public DateTime? LastEventTimestamp { get; set; }
    public int ProductionCount { get; set; } = 0;
    public int? LastCounterValue { get; set; }
    public double CurrentSpeed { get; set; } = 0.0;
    public double AverageSpeed { get; set; } = 0.0;
    public double TimeRunning { get; set; } = 0.0;
    public double TimeStopped { get; set; } = 0.0;
    public double TimeFault { get; set; } = 0.0;
    public double TimeSetup { get; set; } = 0.0;
    public double TotalTime { get; set; } = 0.0;
    public bool AutoStopDetected { get; set; } = false;
}
