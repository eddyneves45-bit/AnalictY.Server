namespace Scada.Api.Services;

internal interface IStateService
{
    object RegisterMachine(string machineId);
    Task<object> ProcessInputAsync(string machineId, int statusWord);
    object GetAllMachineStates();
    ApplicationServiceResult GetMachineState(string machineId);
    object GetConnectionHealth();
    object GetConnectionHealth(string connectionId);
}
