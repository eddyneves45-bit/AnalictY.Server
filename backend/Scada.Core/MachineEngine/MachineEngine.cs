using Microsoft.Extensions.Logging;

namespace Scada.Core.MachineEngine;

public class MachineEngine
{
    private readonly string _machineId;
    private readonly ILogger<MachineEngine> _logger;
    private readonly Func<string, string, Task>? _onStopDetected;
    private readonly Dictionary<string, CauseStats> _causeStats;
    private DateTime? _stopStartTime;
    private string? _currentReason;

    public MachineEngine(
        string machineId,
        ILogger<MachineEngine> logger,
        Func<string, string, Task>? onStopDetected = null)
    {
        _machineId = machineId;
        _logger = logger;
        _onStopDetected = onStopDetected;
        _causeStats = new Dictionary<string, CauseStats>();
    }

    public async Task ProcessStateTransitionAsync(string fromState, string toState, string context)
    {
        if (toState == "stopped" && fromState != "stopped")
        {
            // Máquina parou
            _stopStartTime = DateTime.UtcNow;
            _currentReason = context;
            
            _logger.LogInformation("Máquina {MachineId} parou. Motivo: {Reason}", _machineId, context);

            if (_onStopDetected != null)
            {
                await _onStopDetected(_machineId, context);
            }
        }
        else if (toState != "stopped" && fromState == "stopped")
        {
            // Máquina voltou a rodar
            if (_stopStartTime.HasValue && !string.IsNullOrEmpty(_currentReason))
            {
                var duration = (DateTime.UtcNow - _stopStartTime.Value).TotalSeconds;
                
                _logger.LogInformation(
                    "Máquina {MachineId} voltou a operar. Parada durou {Duration}s. Motivo: {Reason}",
                    _machineId, duration, _currentReason);

                // Atualiza estatísticas
                UpdateCauseStats(_currentReason, duration);
            }

            _stopStartTime = null;
            _currentReason = null;
        }
    }

    private void UpdateCauseStats(string reason, double duration)
    {
        if (!_causeStats.ContainsKey(reason))
        {
            _causeStats[reason] = new CauseStats { Reason = reason };
        }

        _causeStats[reason].Count++;
        _causeStats[reason].TotalDurationSeconds += duration;
        
        // Weight adaptativo: mais frequente = menor weight
        _causeStats[reason].Weight = 1.0 / Math.Sqrt(_causeStats[reason].Count);
    }

    public Dictionary<string, CauseStats> GetCauseStats()
    {
        return _causeStats;
    }
}

public class CauseStats
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public double TotalDurationSeconds { get; set; }
    public double Weight { get; set; } = 1.0;
}
