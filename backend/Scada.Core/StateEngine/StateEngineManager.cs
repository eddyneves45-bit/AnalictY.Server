using Microsoft.Extensions.Logging;

namespace Scada.Core.StateEngine;

public class StateEngineManager
{
    private readonly Dictionary<string, StateEngine> _engines;
    private readonly DelayConfig _defaultDelayConfig;
    private readonly ILogger<StateEngineManager> _logger;

    public StateEngineManager(ILogger<StateEngineManager> logger, DelayConfig? defaultDelayConfig = null)
    {
        _engines = new Dictionary<string, StateEngine>();
        _logger = logger;
        _defaultDelayConfig = defaultDelayConfig ?? new DelayConfig();
    }

    public StateEngine RegisterMachine(
        string machineId,
        Dictionary<string, int>? deriverConfig = null,
        Func<StateTransitionEvent, Task>? onTransition = null)
    {
        if (_engines.ContainsKey(machineId))
        {
            _logger.LogWarning("Máquina {MachineId} já está registrada no State Engine", machineId);
            return _engines[machineId];
        }

        var deriver = new StateDeriver(deriverConfig);
        var engine = new StateEngine(machineId, deriver, _defaultDelayConfig, _logger, onTransition);
        _engines[machineId] = engine;

        _logger.LogInformation("Máquina {MachineId} registrada no State Engine", machineId);
        return engine;
    }

    public void UnregisterMachine(string machineId)
    {
        if (_engines.Remove(machineId))
        {
            _logger.LogInformation("Máquina {MachineId} removida do State Engine", machineId);
        }
    }

    public async Task<StateTransitionEvent?> ProcessInputAsync(string machineId, int statusWord, Dictionary<string, object>? tags = null)
    {
        if (!_engines.TryGetValue(machineId, out var engine))
        {
            _logger.LogWarning("Máquina {MachineId} não encontrada no State Engine", machineId);
            return null;
        }

        return await engine.ProcessInputAsync(statusWord, tags);
    }

    public MachineStateContext? GetStateInfo(string machineId)
    {
        if (!_engines.TryGetValue(machineId, out var engine))
        {
            return null;
        }

        return engine.GetStateInfo();
    }

    public Dictionary<string, MachineStateContext> GetAllStateInfo()
    {
        var result = new Dictionary<string, MachineStateContext>();
        foreach (var (machineId, engine) in _engines)
        {
            result[machineId] = engine.GetStateInfo();
        }
        return result;
    }
}
