namespace Scada.Api.Services;

internal interface ITagConfigService
{
    Task<object> GetMappingsAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateMappingAsync(int machineId, CreateMachineTagMapRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteMappingAsync(int machineId, string role, CancellationToken cancellationToken = default);
    Task<object> GetMachineDowntimeReasonsAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertMachineDowntimeReasonAsync(int machineId, MachineDowntimeReasonRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteMachineDowntimeReasonAsync(int machineId, int code, CancellationToken cancellationToken = default);
    Task<object> GetMachineLossConfigAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertMachineLossConfigAsync(int machineId, MachineLossConfigRequest request, CancellationToken cancellationToken = default);
    Task<object> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateTagAsync(TagConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateTagAsync(int id, TagConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteTagAsync(int id, CancellationToken cancellationToken = default);
}
