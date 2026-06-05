namespace Scada.Core.StateEngine;

public class StateTransitionEvent
{
    public string MachineId { get; set; } = string.Empty;
    public MachineState FromState { get; set; }
    public MachineState ToState { get; set; }
    public MachineContext FromContext { get; set; }
    public MachineContext ToContext { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double Duration { get; set; } // em segundos
    public string EventId { get; set; } = Guid.NewGuid().ToString(); // UUID para idempotência
    public int StatusWord { get; set; }
    public string Source { get; set; } = string.Empty;
}
