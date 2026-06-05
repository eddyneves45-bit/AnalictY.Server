namespace Scada.Api.Services;

internal interface IAlertRuleService
{
    Task<object> ListAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpdateAsync(int id, AlertRuleRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
