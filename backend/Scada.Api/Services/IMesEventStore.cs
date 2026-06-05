namespace Scada.Api.Services;

internal interface IMesEventStore
{
    Task ProcessAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
}
