namespace Scada.Core.Models.MySQL;

public class MachineState
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string CandidateState { get; set; } = string.Empty;
    public string CandidateContext { get; set; } = string.Empty;
    public DateTime StateStartTime { get; set; } = DateTime.UtcNow;
    public DateTime? CandidateStartTime { get; set; }
    public int LastStatusWord { get; set; } = 0;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public string? MetadataJson { get; set; }
}
