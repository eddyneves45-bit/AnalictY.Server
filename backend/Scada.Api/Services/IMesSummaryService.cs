namespace Scada.Api.Services;

internal interface IMesSummaryService
{
    Task RebuildMachineDayAsync(string machineId, DateOnly date, CancellationToken cancellationToken = default);
}
