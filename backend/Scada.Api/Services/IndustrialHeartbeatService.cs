using System.Collections.Concurrent;

namespace Scada.Api.Services;

internal sealed class IndustrialHeartbeatService : IIndustrialHeartbeatService
{
    private static readonly TimeSpan ConnectionStaleAfter = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<int, TagHeartbeat> _tags = new();
    private readonly ConcurrentDictionary<string, ConnectionHeartbeat> _connections = new();
    private readonly ConcurrentQueue<IndustrialError> _recentErrors = new();

    public void RegisterTag(int tagId, string tagName, string driverType, int? connectionId, int expectedIntervalMs)
    {
        _tags.AddOrUpdate(
            tagId,
            _ => new TagHeartbeat(tagId, tagName, driverType, connectionId, expectedIntervalMs, null, "UNKNOWN"),
            (_, existing) => existing with
            {
                TagName = tagName,
                DriverType = driverType,
                ConnectionId = connectionId,
                ExpectedIntervalMs = expectedIntervalMs
            });
    }

    public void UnregisterTag(int tagId)
    {
        _tags.TryRemove(tagId, out _);
    }

    public void RegisterConnection(string driverType, int connectionId)
    {
        var key = BuildConnectionKey(driverType, connectionId);
        _connections.TryAdd(key, new ConnectionHeartbeat(driverType, connectionId, null));
    }

    public void RecordTag(TagValueEnvelope envelope)
    {
        _tags.AddOrUpdate(
            envelope.TagId,
            _ => new TagHeartbeat(
                envelope.TagId,
                envelope.TagName,
                envelope.DriverType,
                envelope.ConnectionId,
                1000,
                envelope.ReceivedAt,
                envelope.Quality),
            (_, existing) => existing with
            {
                TagName = envelope.TagName,
                DriverType = envelope.DriverType,
                ConnectionId = envelope.ConnectionId,
                LastUpdate = envelope.ReceivedAt,
                Quality = envelope.Quality
            });
    }

    public void RecordConnection(string driverType, int connectionId, DateTime receivedAt)
    {
        var key = BuildConnectionKey(driverType, connectionId);
        _connections[key] = new ConnectionHeartbeat(driverType, connectionId, receivedAt);
    }

    public void RecordError(string source, string message)
    {
        _recentErrors.Enqueue(new IndustrialError(source, message, DateTime.UtcNow));
        while (_recentErrors.Count > 50 && _recentErrors.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<int> GetStaleTagIds()
    {
        var now = DateTime.UtcNow;
        return _tags.Values
            .Where(item => item.LastUpdate.HasValue && now - item.LastUpdate.Value > GetTagStaleAfter(item))
            .Select(item => item.TagId)
            .ToList();
    }

    public object GetSnapshot()
    {
        var now = DateTime.UtcNow;
        return new
        {
            tags = _tags.Values
                .OrderBy(item => item.TagId)
                .Select(item => new
                {
                    tag_id = item.TagId,
                    tag_name = item.TagName,
                    driver_type = item.DriverType,
                    connection_id = item.ConnectionId,
                    expected_interval_ms = item.ExpectedIntervalMs,
                    stale_after_ms = GetTagStaleAfter(item).TotalMilliseconds,
                    quality = item.Quality,
                    last_update = item.LastUpdate,
                    age_ms = item.LastUpdate.HasValue ? (double?)(now - item.LastUpdate.Value).TotalMilliseconds : null,
                    status = GetTagStatus(item, now)
                }),
            connections = _connections.Values
                .OrderBy(item => item.DriverType)
                .ThenBy(item => item.ConnectionId)
                .Select(item =>
                {
                    var relatedTags = _tags.Values
                        .Where(tag => tag.ConnectionId == item.ConnectionId && string.Equals(tag.DriverType, item.DriverType, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    return new
                    {
                        driver_type = item.DriverType,
                        connection_id = item.ConnectionId,
                        last_message = item.LastMessage,
                        age_ms = item.LastMessage.HasValue ? (double?)(now - item.LastMessage.Value).TotalMilliseconds : null,
                        status = GetConnectionStatus(item, now),
                        tags_total = relatedTags.Count,
                        tags_online = relatedTags.Count(tag => GetTagStatus(tag, now) == "online"),
                        tags_stale = relatedTags.Count(tag => GetTagStatus(tag, now) == "stale"),
                        tags_bad = relatedTags.Count(tag => GetTagStatus(tag, now) == "bad"),
                        tags_never_received = relatedTags.Count(tag => GetTagStatus(tag, now) == "never_received")
                    };
                }),
            recent_errors = _recentErrors.ToArray()
        };
    }

    public IndustrialHeartbeatMetrics GetMetricsSnapshot()
    {
        var now = DateTime.UtcNow;
        var tagStatuses = _tags.Values
            .Select(tag => GetTagStatus(tag, now))
            .ToList();

        var connectionStatuses = _connections.Values
            .Select(connection => GetConnectionStatus(connection, now))
            .ToList();

        return new IndustrialHeartbeatMetrics(
            _tags.Count,
            tagStatuses.Count(status => status == "online"),
            tagStatuses.Count(status => status == "stale"),
            tagStatuses.Count(status => status == "bad"),
            tagStatuses.Count(status => status == "never_received"),
            _connections.Count,
            connectionStatuses.Count(status => status == "online"),
            connectionStatuses.Count(status => status == "stale"),
            connectionStatuses.Count(status => status == "offline"));
    }

    private static string BuildConnectionKey(string driverType, int connectionId) => $"{driverType}:{connectionId}";
    private static TimeSpan GetTagStaleAfter(TagHeartbeat tag)
    {
        var expectedMs = Math.Max(tag.ExpectedIntervalMs, 1000);
        return TimeSpan.FromMilliseconds(Math.Max(expectedMs * 3L, 15_000));
    }

    private static string GetTagStatus(TagHeartbeat tag, DateTime now)
    {
        if (!tag.LastUpdate.HasValue)
        {
            return "never_received";
        }

        if (!string.Equals(tag.Quality, "GOOD", StringComparison.OrdinalIgnoreCase))
        {
            return "bad";
        }

        return now - tag.LastUpdate.Value > GetTagStaleAfter(tag) ? "stale" : "online";
    }

    private static string GetConnectionStatus(ConnectionHeartbeat connection, DateTime now)
    {
        if (!connection.LastMessage.HasValue)
        {
            return "offline";
        }

        return now - connection.LastMessage.Value > ConnectionStaleAfter ? "stale" : "online";
    }

    private sealed record TagHeartbeat(
        int TagId,
        string TagName,
        string DriverType,
        int? ConnectionId,
        int ExpectedIntervalMs,
        DateTime? LastUpdate,
        string Quality);

    private sealed record ConnectionHeartbeat(
        string DriverType,
        int ConnectionId,
        DateTime? LastMessage);

    private sealed record IndustrialError(
        string Source,
        string Message,
        DateTime Timestamp);
}
