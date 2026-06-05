namespace Scada.Api.Services;

internal interface IMachineGoalService
{
    Task<object> ListAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateAsync(int machineId, MachineGoalRequest request, CancellationToken cancellationToken = default);
}
