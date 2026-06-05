namespace Scada.Core.StateEngine;

public class DelayConfig
{
    public double RunningToStopped { get; set; } = 60.0; // segundos
    public double StoppedToRunning { get; set; } = 10.0; // segundos
    public double IdleToStopped { get; set; } = 60.0; // segundos
    public double StoppedToIdle { get; set; } = 10.0; // segundos
    public double RunningToIdle { get; set; } = 60.0; // segundos
    public double IdleToRunning { get; set; } = 10.0; // segundos
    public bool FaultImmediate { get; set; } = true; // FAULT/EMERGENCY são imediatos
    public bool EmergencyImmediate { get; set; } = true; // FAULT/EMERGENCY são imediatos
}
