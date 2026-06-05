namespace Scada.Core.StateEngine;

public class MachineStateContext
{
    public string MachineId { get; set; } = string.Empty;
    public MachineState CurrentState { get; set; }
    public MachineContext CurrentContext { get; set; }
    public MachineState? CandidateState { get; set; }
    public MachineContext? CandidateContext { get; set; }
    public DateTime StateStartTime { get; set; } = DateTime.UtcNow;
    public DateTime? CandidateStartTime { get; set; }
    public int LastStatusWord { get; set; }
}
