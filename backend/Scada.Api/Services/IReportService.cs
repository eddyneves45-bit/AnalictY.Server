namespace Scada.Api.Services;

internal interface IReportService
{
    Task<object> ListReportsAsync(
        string? machineId,
        string? reportType,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<object> CreateReportAsync(ReportCreateRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateReportAsync(int id, ReportUpdateRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteReportAsync(int id, CancellationToken cancellationToken = default);
    Task<object> GenerateAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<object> ScheduleAsync(ReportScheduleRequest request, CancellationToken cancellationToken = default);
    Task<object> ListSchedulesAsync(string? machineId, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateScheduleAsync(long id, ReportScheduleUpdateRequest request, CancellationToken cancellationToken = default);
    Task<object> GetProductionMatrixAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<object> GetStatusMatrixAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<object> GetDowntimeEventsAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<string> ExportProductionCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<string> ExportCsvAsync(ReportGenerateRequest request, CancellationToken cancellationToken = default);
    Task<object> GetMachineDashboardAsync(string machineId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<object> ListExecutionsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteExecutionAsync(long id, CancellationToken cancellationToken = default);
    Task ExecuteDueSchedulesAsync(CancellationToken cancellationToken = default);
}
