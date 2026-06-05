namespace Scada.Api.Services;

internal interface IConfigApplicationService
{
    Task<object> GetOpcuaConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertOpcuaConfigAsync(OpcuaConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteOpcuaConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> BrowseOpcuaAsync(string? nodeId, int? connectionId, CancellationToken cancellationToken = default);

    Task<object> GetMqttConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertMqttConfigAsync(MqttConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteMqttConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestMqttConfigAsync(int id, CancellationToken cancellationToken = default);

    Task<object> GetMySqlConfigsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertMySqlConfigAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteMySqlConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetPrimaryMySqlConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> SetLocalMySqlConfigAsync(int id, bool isLocal, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestMySqlConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> TestMySqlRequestAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> InitMySqlConfigAsync(int id, CancellationToken cancellationToken = default);

    Task<object> GetTagMappingsAsync(int machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateTagMappingAsync(int machineId, CreateMachineTagMapRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteTagMappingAsync(int machineId, string role, CancellationToken cancellationToken = default);
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
