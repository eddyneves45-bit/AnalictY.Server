using Microsoft.Extensions.Logging;

namespace Scada.Core.StateEngine;

public class StateEngine
{
    private readonly string _machineId;
    private readonly StateDeriver _deriver;
    private readonly DelayConfig _delayConfig;
    private readonly MachineStateContext _context;
    private readonly ILogger _logger;
    private readonly Func<StateTransitionEvent, Task>? _onTransition;

    public StateEngine(
        string machineId,
        StateDeriver deriver,
        DelayConfig delayConfig,
        ILogger logger,
        Func<StateTransitionEvent, Task>? onTransition = null)
    {
        _machineId = machineId;
        _deriver = deriver;
        _delayConfig = delayConfig;
        _logger = logger;
        _onTransition = onTransition;
        _context = new MachineStateContext
        {
            MachineId = machineId,
            CurrentState = MachineState.Stopped,
            CurrentContext = MachineContext.NoDemand,
            StateStartTime = DateTime.UtcNow
        };
    }

    public async Task<StateTransitionEvent?> ProcessInputAsync(int statusWord, Dictionary<string, object>? tags = null)
    {
        var (newState, newContext) = _deriver.Derive(statusWord, tags);
        _context.LastStatusWord = statusWord;

        // Se o estado não mudou, nada a fazer
        if (newState == _context.CurrentState && newContext == _context.CurrentContext)
        {
            return null;
        }

        // Se já temos um candidato, verifica se é o mesmo
        if (_context.CandidateState.HasValue && _context.CandidateState == newState && 
            _context.CandidateContext.HasValue && _context.CandidateContext == newContext)
        {
            // Verifica se o delay já passou
            if (_context.CandidateStartTime.HasValue)
            {
                var elapsed = (DateTime.UtcNow - _context.CandidateStartTime.Value).TotalSeconds;
                var delay = GetDelay(_context.CurrentState, newState, newContext);

                if (elapsed >= delay)
                {
                    // Confirma a transição
                    return await ConfirmTransitionAsync(newState, newContext);
                }
            }
            return null;
        }

        // Cria novo candidato
        _context.CandidateState = newState;
        _context.CandidateContext = newContext;
        _context.CandidateStartTime = DateTime.UtcNow;

        // Se delay for 0, confirma imediatamente
        var immediateDelay = GetDelay(_context.CurrentState, newState, newContext);
        if (immediateDelay == 0)
        {
            return await ConfirmTransitionAsync(newState, newContext);
        }

        return null;
    }

    private async Task<StateTransitionEvent> ConfirmTransitionAsync(MachineState newState, MachineContext newContext)
    {
        var now = DateTime.UtcNow;
        var duration = (now - _context.StateStartTime).TotalSeconds;

        var transitionEvent = new StateTransitionEvent
        {
            MachineId = _machineId,
            FromState = _context.CurrentState,
            ToState = newState,
            FromContext = _context.CurrentContext,
            ToContext = newContext,
            StartTime = _context.StateStartTime,
            EndTime = now,
            Duration = duration,
            StatusWord = _context.LastStatusWord
        };

        _logger.LogInformation(
            "Máquina {MachineId}: {FromState}→{ToState} ({FromContext}→{ToContext}), Duração: {Duration}s",
            _machineId, _context.CurrentState, newState, _context.CurrentContext, newContext, duration);

        // Atualiza contexto
        _context.CurrentState = newState;
        _context.CurrentContext = newContext;
        _context.StateStartTime = now;
        _context.CandidateState = null;
        _context.CandidateContext = null;
        _context.CandidateStartTime = null;

        // Chama callback se existir
        if (_onTransition != null)
        {
            await _onTransition(transitionEvent);
        }

        return transitionEvent;
    }

    private double GetDelay(MachineState fromState, MachineState toState, MachineContext toContext)
    {
        // FAULT e EMERGENCY são imediatos
        if (toContext == MachineContext.Fault && _delayConfig.FaultImmediate)
            return 0;
        if (toContext == MachineContext.Emergency && _delayConfig.EmergencyImmediate)
            return 0;

        return (fromState, toState) switch
        {
            (MachineState.Running, MachineState.Stopped) => _delayConfig.RunningToStopped,
            (MachineState.Stopped, MachineState.Running) => _delayConfig.StoppedToRunning,
            (MachineState.Idle, MachineState.Stopped) => _delayConfig.IdleToStopped,
            (MachineState.Stopped, MachineState.Idle) => _delayConfig.StoppedToIdle,
            (MachineState.Running, MachineState.Idle) => _delayConfig.RunningToIdle,
            (MachineState.Idle, MachineState.Running) => _delayConfig.IdleToRunning,
            _ => 10.0 // Default
        };
    }

    public MachineStateContext GetStateInfo()
    {
        return _context;
    }
}
