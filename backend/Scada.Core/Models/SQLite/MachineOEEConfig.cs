namespace Scada.Core.Models.SQLite;

public class MachineOEEConfig
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public double IdealSpeed { get; set; } = 0.0; // velocidade ideal para cálculo de performance
    public double Quality { get; set; } = 1.0; // qualidade para cálculo de OEE
    public int MicroStopThreshold { get; set; } = 30; // segundos
    public int LongStopThreshold { get; set; } = 300; // segundos (5 minutos)
    public int NoDataThreshold { get; set; } = 600; // segundos (10 minutos)
    public bool IncludeMicroStopsInOEE { get; set; } = false;
    public string LossSource { get; set; } = "tag";
    public double FixedLossValue { get; set; } = 0.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
