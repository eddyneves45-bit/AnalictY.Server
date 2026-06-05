namespace Scada.Api.Services;

internal class ConfigApplicationService : IConfigApplicationService
{
    private readonly IOpcuaConfigService _opcuaConfigService;
    private readonly IMqttConfigService _mqttConfigService;
    private readonly IMySqlConfigService _mySqlConfigService;
    private readonly ITagConfigService _tagConfigService;

    public ConfigApplicationService(
        IOpcuaConfigService opcuaConfigService,
        IMqttConfigService mqttConfigService,
        IMySqlConfigService mySqlConfigService,
        ITagConfigService tagConfigService)
    {
        _opcuaConfigService = opcuaConfigService;
        _mqttConfigService = mqttConfigService;
        _mySqlConfigService = mySqlConfigService;
        _tagConfigService = tagConfigService;
    }

    public Task<object> GetOpcuaConfigsAsync(CancellationToken cancellationToken = default)
    {
        return _opcuaConfigService.GetConfigsAsync(cancellationToken);
    }

    public Task<ApplicationServiceResult> UpsertOpcuaConfigAsync(OpcuaConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _opcuaConfigService.UpsertConfigAsync(request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteOpcuaConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _opcuaConfigService.DeleteConfigAsync(id, cancellationToken);
    }

    public Task<ApplicationServiceResult> BrowseOpcuaAsync(string? nodeId, int? connectionId, CancellationToken cancellationToken = default)
    {
        return _opcuaConfigService.BrowseAsync(nodeId, connectionId, cancellationToken);
    }

    public Task<object> GetMqttConfigsAsync(CancellationToken cancellationToken = default)
    {
        return _mqttConfigService.GetConfigsAsync(cancellationToken);
    }

    public Task<ApplicationServiceResult> UpsertMqttConfigAsync(MqttConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _mqttConfigService.UpsertConfigAsync(request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteMqttConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mqttConfigService.DeleteConfigAsync(id, cancellationToken);
    }

    public Task<ApplicationServiceResult> TestMqttConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mqttConfigService.TestConfigAsync(id, cancellationToken);
    }

    public Task<object> GetMySqlConfigsAsync(CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.GetConfigsAsync(cancellationToken);
    }

    public Task<ApplicationServiceResult> UpsertMySqlConfigAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.UpsertConfigAsync(request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteMySqlConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.DeleteConfigAsync(id, cancellationToken);
    }

    public Task<ApplicationServiceResult> SetPrimaryMySqlConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.SetPrimaryConfigAsync(id, cancellationToken);
    }

    public Task<ApplicationServiceResult> SetLocalMySqlConfigAsync(int id, bool isLocal, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.SetLocalConfigAsync(id, isLocal, cancellationToken);
    }

    public Task<ApplicationServiceResult> TestMySqlConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.TestConfigAsync(id, cancellationToken);
    }

    public Task<ApplicationServiceResult> TestMySqlRequestAsync(MySqlConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.TestRequestAsync(request, cancellationToken);
    }

    public Task<ApplicationServiceResult> InitMySqlConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        return _mySqlConfigService.InitConfigAsync(id, cancellationToken);
    }

    public Task<object> GetTagMappingsAsync(int machineId, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.GetMappingsAsync(machineId, cancellationToken);
    }

    public Task<ApplicationServiceResult> CreateTagMappingAsync(int machineId, CreateMachineTagMapRequest request, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.CreateMappingAsync(machineId, request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteTagMappingAsync(int machineId, string role, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.DeleteMappingAsync(machineId, role, cancellationToken);
    }

    public Task<object> GetMachineDowntimeReasonsAsync(int machineId, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.GetMachineDowntimeReasonsAsync(machineId, cancellationToken);
    }

    public Task<ApplicationServiceResult> UpsertMachineDowntimeReasonAsync(int machineId, MachineDowntimeReasonRequest request, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.UpsertMachineDowntimeReasonAsync(machineId, request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteMachineDowntimeReasonAsync(int machineId, int code, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.DeleteMachineDowntimeReasonAsync(machineId, code, cancellationToken);
    }

    public Task<object> GetMachineLossConfigAsync(int machineId, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.GetMachineLossConfigAsync(machineId, cancellationToken);
    }

    public Task<ApplicationServiceResult> UpsertMachineLossConfigAsync(int machineId, MachineLossConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.UpsertMachineLossConfigAsync(machineId, request, cancellationToken);
    }

    public Task<object> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        return _tagConfigService.GetTagsAsync(cancellationToken);
    }

    public Task<ApplicationServiceResult> CreateTagAsync(TagConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.CreateTagAsync(request, cancellationToken);
    }

    public Task<ApplicationServiceResult> UpdateTagAsync(int id, TagConfigRequest request, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.UpdateTagAsync(id, request, cancellationToken);
    }

    public Task<ApplicationServiceResult> DeleteTagAsync(int id, CancellationToken cancellationToken = default)
    {
        return _tagConfigService.DeleteTagAsync(id, cancellationToken);
    }
}
