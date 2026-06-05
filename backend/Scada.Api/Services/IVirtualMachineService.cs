namespace Scada.Api.Services;

internal interface IVirtualMachineService
{
    Task<object> ListAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateAsync(VirtualMachineCreateRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> GetAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> PublishAsync(int machineId, VirtualMachineCommandRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> StartAsync(int machineId, VirtualMachineStartRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> StopAsync(int machineId, CancellationToken cancellationToken = default);
}
