namespace Scada.Api.Services;

internal sealed record TagValueEnvelope(
    int TagId,
    string TagName,
    string DriverType,
    string PersistenceMode,
    int? ConnectionId,
    object? Value,
    string Quality,
    DateTime SourceTimestamp,
    DateTime ReceivedAt,
    string Source);
