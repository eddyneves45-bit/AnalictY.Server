namespace Scada.Api.Services;

internal interface IOeeApplicationService
{
    Task<object> GetResolvedStatesAsync(CancellationToken cancellationToken = default);
    Task<object> GetAllMachinesOeeAsync(CancellationToken cancellationToken = default);
    Task<object?> GetMachineOeeAsync(string machineId, CancellationToken cancellationToken = default);
    Task<object> GetMachineStopsAsync(string machineId, int limit, CancellationToken cancellationToken = default);
    Task<object> SetIdealSpeedAsync(IdealSpeedRequest request, CancellationToken cancellationToken = default);
    Task<object> SetQualityAsync(QualityRequest request, CancellationToken cancellationToken = default);
    Task<object> SetStopThresholdsAsync(StopThresholdsRequest request, CancellationToken cancellationToken = default);
}
