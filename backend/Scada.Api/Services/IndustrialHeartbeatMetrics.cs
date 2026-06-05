namespace Scada.Api.Services;

internal sealed record IndustrialHeartbeatMetrics(
    int TagsTotal,
    int TagsOnline,
    int TagsStale,
    int TagsBad,
    int TagsNeverReceived,
    int ConnectionsTotal,
    int ConnectionsOnline,
    int ConnectionsStale,
    int ConnectionsOffline);
