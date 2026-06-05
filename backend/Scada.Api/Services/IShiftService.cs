namespace Scada.Api.Services;

internal interface IShiftService
{
    Task<object> ListAsync(CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> UpsertAsync(ShiftRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationServiceResult> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
