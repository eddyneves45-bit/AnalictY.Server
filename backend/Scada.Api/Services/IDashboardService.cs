namespace Scada.Api.Services;

internal interface IDashboardService
{
    Task<object> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<object> GetMachineStatusAsync(string machineId, CancellationToken cancellationToken = default);
}
