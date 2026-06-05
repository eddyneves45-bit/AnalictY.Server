using Scada.Core.Quality;
using Scada.Core.StateEngine;

namespace Scada.Api.Services;

internal class StateService : IStateService
{
    private readonly StateEngineManager _stateEngineManager;
    private readonly QualityProcessor _qualityProcessor;

    public StateService(StateEngineManager stateEngineManager, QualityProcessor qualityProcessor)
    {
        _stateEngineManager = stateEngineManager;
        _qualityProcessor = qualityProcessor;
    }

    public object RegisterMachine(string machineId)
    {
        _stateEngineManager.RegisterMachine(machineId);
        return new { message = $"Máquina {machineId} registrada" };
    }

    public async Task<object> ProcessInputAsync(string machineId, int statusWord)
    {
        await _stateEngineManager.ProcessInputAsync(machineId, statusWord);
        return new { message = $"Input processado para máquina {machineId}" };
    }

    public object GetAllMachineStates()
    {
        return _stateEngineManager.GetAllStateInfo();
    }

    public ApplicationServiceResult GetMachineState(string machineId)
    {
        var state = _stateEngineManager.GetStateInfo(machineId);
        return state != null ? ApplicationServiceResult.Ok(state) : ApplicationServiceResult.NotFound();
    }

    public object GetConnectionHealth()
    {
        return _qualityProcessor.GetAllConnectionHealth();
    }

    public object GetConnectionHealth(string connectionId)
    {
        return _qualityProcessor.GetConnectionHealth(connectionId);
    }
}
