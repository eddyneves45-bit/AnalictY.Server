namespace Scada.Api.Services;

internal interface IVirtualMachineRuntimeService
{
    VirtualMachineRuntimeSnapshot GetOrCreate(int machineId, IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags);
    VirtualMachineRuntimeSnapshot? Get(int machineId);
    VirtualMachineRuntimeSnapshot Update(
        int machineId,
        IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags,
        int status,
        int downtimeReasonCode,
        double productionCounter,
        double lossCounter);
    VirtualMachineRuntimeSnapshot Start(int machineId, IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags, int piecesPerMinute);
    VirtualMachineRuntimeSnapshot? Stop(int machineId);
    IReadOnlyList<VirtualMachineRuntimeState> GetRunningStates();
}

internal sealed record VirtualMachineRuntimeTag(int Id, string Name, string DriverType, string PersistenceMode);

internal sealed record VirtualMachineRuntimeSnapshot(
    int MachineId,
    int Status,
    int DowntimeReasonCode,
    double ProductionCounter,
    double LossCounter,
    int PiecesPerMinute,
    bool Running);

