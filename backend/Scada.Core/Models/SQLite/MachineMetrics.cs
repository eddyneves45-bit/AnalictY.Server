namespace Scada.Core.Models.SQLite;

public class MachineMetrics
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty; // hourly, daily, weekly
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    
    // Métricas agregadas
    public int ProductionCount { get; set; } = 0;
    public double TimeRunning { get; set; } = 0.0;
    public double TimeStopped { get; set; } = 0.0;
    public double TimeFault { get; set; } = 0.0;
    public double TimeSetup { get; set; } = 0.0;
    public double TotalTime { get; set; } = 0.0;
    
    // OEE
    public double Availability { get; set; } = 0.0;
    public double Performance { get; set; } = 0.0;
    public double Quality { get; set; } = 1.0;
    public double Oee { get; set; } = 0.0;
    
    // Velocidade
    public double AverageSpeed { get; set; } = 0.0;
    public double MaxSpeed { get; set; } = 0.0;
    public double MinSpeed { get; set; } = 0.0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
