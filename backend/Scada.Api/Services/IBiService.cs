namespace Scada.Api.Services;

internal interface IBiService
{
    Task<object> GetIndicatorsAsync(
        string? costCenter,
        string? machineId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);

    Task<object> GetCostCentersAsync(CancellationToken cancellationToken = default);
    Task<object> GetMachinesAsync(string costCenter, CancellationToken cancellationToken = default);
    Task<object> GetMachineOverviewAsync(string machineId, DateTime from, DateTime to, string? targetMode = null, CancellationToken cancellationToken = default);
    Task<object> GetMachineSummariesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<object> GetMachineProductionByShiftAsync(string machineId, DateOnly date, CancellationToken cancellationToken = default);
    Task<string> ExportProductionCsvAsync(string? machineId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
    Task<string> ExportDowntimeCsvAsync(string? machineId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
}
