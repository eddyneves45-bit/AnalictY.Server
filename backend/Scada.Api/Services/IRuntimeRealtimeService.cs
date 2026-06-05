namespace Scada.Api.Services;

internal interface IRuntimeRealtimeService
{
    IReadOnlyList<object> BuildSnapshot();
    Task PublishAsync(int tagId, CancellationToken cancellationToken = default);
}
