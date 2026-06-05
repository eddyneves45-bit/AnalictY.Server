namespace Scada.Api.Services;

internal interface IMachineService
{
    Task<object> GetMachinesAsync(CancellationToken cancellationToken = default);
    Task<object> GetFoldersAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> GetMachineAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateFolderAsync(MachineFolderRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateFolderAsync(int id, MachineFolderRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteFolderAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateMachineAsync(MachineRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateMachineAsync(int id, MachineRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteMachineAsync(int id, CancellationToken cancellationToken = default);
}
